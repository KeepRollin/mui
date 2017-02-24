using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FirstFloor.ModernUI.Windows.Controls;

namespace FirstFloor.ModernUI.Windows.Navigation
{
    /// <summary>
    /// Default implementation of NavigationService.
    /// </summary>
    public class DefaultNavigationService : IModernNavigationService<ModernFrame>
    {
        private readonly Stack<Uri> _history = new Stack<Uri>();
        private readonly Dictionary<Uri, object> _contentCache = new Dictionary<Uri, object>();

        /// <summary>
        /// Occurs when navigation to a content fragment begins.
        /// </summary>
        public event EventHandler<FragmentNavigationEventArgs> FragmentNavigation;
        /// <summary>
        /// Occurs when a new navigation is requested.
        /// </summary>
        /// <remarks>
        /// The navigating event is also raised when a parent frame is navigating. This allows for cancelling parent navigation.
        /// </remarks>
        public event EventHandler<NavigatingCancelEventArgs> Navigating;
        /// <summary>
        /// Occurs when navigation to new content has completed.
        /// </summary>
        public event EventHandler<NavigationEventArgs> Navigated;
        /// <summary>
        /// Occurs when navigation has failed.
        /// </summary>
        public event EventHandler<NavigationFailedEventArgs> NavigationFailed;
                
        /// <summary>
        /// Default constructor for DependencyProperty.
        /// </summary>
        public DefaultNavigationService(ModernFrame frame)
        {
            Frame = frame;
        }

        /// <summary>
        /// Performs navigation against current ModernFrame.
        /// </summary>        
        /// <param name="oldValue">Navigate From Uri</param>
        /// <param name="newValue">Navigate To Uri</param>
        /// <param name="navigationType">Type of Navigation</param>
        /// <param name="passingParameter">Parameter for passing.</param>
        /// <remarks>
        /// Can be used to fire Navigated events.
        /// </remarks>
        public void Navigate<TK>(Uri oldValue, Uri newValue, NavigationType navigationType, TK passingParameter)
        {
            Debug.WriteLine("Navigating from '{0}' to '{1}'", oldValue, newValue);

            // set IsLoadingContent state
            Frame.SetValue(ModernFrame.IsLoadingContentPropertyKey, true);

            // cancel previous load content task (if any)
            // note: no need for thread synchronization, this code always executes on the UI thread
            if (Frame.TokenSource != null)
            {
                Frame.TokenSource.Cancel();
                Frame.TokenSource = null;
            }

            // push previous source onto the history stack (only for new navigation types)
            if (oldValue != null && navigationType == NavigationType.New)
            {
                _history.Push(oldValue);
            }

            object newContent = null;

            if (newValue != null)
            {
                // content is cached on uri without fragment
                var newValueNoFragment = NavigationHelper.RemoveFragment(newValue);

                if (navigationType == NavigationType.Refresh || !this._contentCache.TryGetValue(newValueNoFragment, out newContent))
                {
                    var localTokenSource = new CancellationTokenSource();
                    Frame.TokenSource = localTokenSource;
                    // load the content (asynchronous!)
                    var scheduler = TaskScheduler.FromCurrentSynchronizationContext();

                    var task = Frame.ContentLoader.LoadContentAsync(newValue, Frame.TokenSource.Token);

                    task.ContinueWith(t => {
                        try
                        {
                            if (t.IsCanceled || localTokenSource.IsCancellationRequested)
                            {
                                Debug.WriteLine("Cancelled navigation to '{0}'", newValue);
                            }
                            else if (t.IsFaulted && t.Exception != null)
                            {
                                var failedArgs = new NavigationFailedEventArgs
                                {
                                    Frame = Frame,
                                    Source = newValue,
                                    Error = t.Exception.InnerException,
                                    Handled = false
                                };

                                OnNavigationFailed(failedArgs);
                                // if not handled, show error as content
                                newContent = failedArgs.Handled ? null : failedArgs.Error;
                                SetContent(newValue, navigationType, newContent, true, passingParameter);
                            }
                            else
                            {
                                newContent = t.Result;

                                if (ShouldKeepContentAlive(newContent))
                                {
                                    // keep the new content in memory
                                    _contentCache[newValueNoFragment] = newContent;
                                }

                                SetContent(newValue, navigationType, newContent, false, passingParameter);
                            }
                        }
                        finally
                        {
                            // clear global tokenSource to avoid a Cancel on a disposed object
                            if (Frame.TokenSource == localTokenSource)
                            {
                                Frame.TokenSource = null;
                            }

                            // and dispose of the local tokensource
                            localTokenSource.Dispose();
                        }
                    }, scheduler);
                    return;
                }
            }

            // newValue is null or newContent was found in the cache
            SetContent(newValue, navigationType, newContent, false, passingParameter);
        }

        /// <summary>
        /// The reference of current ModernFrame on which navigation is performed.
        /// </summary>
        public ModernFrame Frame { get; }

        /// <summary>
        /// Checks the possibility of browsing back.
        /// </summary>
        /// <returns>Result of check</returns>
        public bool CanBrowseBack()
        {
            return _history.Count > 0;
        }


        /// <summary>
        /// Navigates back in history.
        /// </summary>
        public void BrowseBack()
        {
            if (_history.Count <= 0) return;

            var oldValue = Frame.Source;
            var newValue = _history.Peek();     // do not remove just yet, navigation may be cancelled

            if (!CanNavigate(oldValue, newValue, NavigationType.Back)) return;

            Frame.IsNavigatingHistory = true;
            Frame.SetCurrentValue(ModernFrame.SourceProperty, _history.Pop());
            Frame.IsNavigatingHistory = false;
        }


        /// <summary>
        /// Requests permission for Navigating to new Uri
        /// </summary>       
        /// <param name="oldValue">Navigating From Uri</param>
        /// <param name="newValue">Navigating To Uri</param>
        /// <param name="navigationType">Type of Navigation</param>
        /// <returns>false if Navigating is cancelled</returns>
        /// <remarks>
        /// Used to allow cancel of action via Navigating event.
        /// </remarks>
        public bool CanNavigate(Uri oldValue, Uri newValue, NavigationType navigationType)
        {
            if (Frame == null) throw new MissingFieldException("Frame");

            var cancelArgs = new NavigatingCancelEventArgs
            {
                Frame = Frame,
                Source = newValue,
                IsParentFrameNavigating = true,
                NavigationType = navigationType,
                Cancel = false,
            };

            var content = Frame.Content as ContentControl;
            if (content != null)
                OnNavigating(content, cancelArgs);

            // check if navigation cancelled
            if (!cancelArgs.Cancel) return true;

            Debug.WriteLine("Cancelled navigation from '{0}' to '{1}'", oldValue, newValue);

            if (Frame.Source != oldValue)
            {
                // enqueue the operation to reset the source back to the old value
                Frame.Dispatcher.BeginInvoke((Action)(() => {
                    Frame.IsResetSource = true;
                    Frame.SetCurrentValue(ModernFrame.SourceProperty, oldValue);
                    Frame.IsResetSource = false;
                }));
            }
            return false;
        }

        /// <summary>
        /// Performs navigation against current ModernFrame.
        /// </summary>
        /// <param name="oldValue">Navigate From Uri</param>
        /// <param name="newValue">Navigate To Uri</param>
        /// <remarks>
        /// Can be used to fire Navigated events.
        /// </remarks>
        public void Navigate(Uri oldValue, Uri newValue)
        {
            // if resetting source or old source equals new, don't do anything
            if (Frame.IsResetSource || newValue != null && newValue.Equals(oldValue))
            {
                return;
            }

            // handle fragment navigation
            string newFragment = null;
            var oldValueNoFragment = NavigationHelper.RemoveFragment(oldValue);
            var newValueNoFragment = NavigationHelper.RemoveFragment(newValue, out newFragment);

            if (newValueNoFragment != null && newValueNoFragment.Equals(oldValueNoFragment))
            {
                // fragment navigation
                var args = new FragmentNavigationEventArgs
                {
                    Fragment = newFragment
                };
                OnFragmentNavigation(Frame.Content as IContent, args);
            }
            else
            {
                var navType = Frame.IsNavigatingHistory ? NavigationType.Back : NavigationType.New;

                // only invoke CanNavigate for new navigation
                if (!CanNavigate(oldValue, newValue, navType)) return;

                Navigate(oldValue, newValue, navType);
            }
        }

        /// <summary>
        /// Performs navigation against current ModernFrame.
        /// </summary>        
        /// <param name="oldValue">Navigate From Uri</param>
        /// <param name="newValue">Navigate To Uri</param>
        /// <param name="navigationType">Type of Navigation</param>
        /// <remarks>
        /// Can be used to fire Navigated events.
        /// </remarks>
        public void Navigate(Uri oldValue, Uri newValue, NavigationType navigationType)
        {
            Navigate<object>(oldValue, newValue, navigationType, null);
        }

        /// <summary>
        /// Refresh current content.
        /// </summary>
        public void Refresh()
        {
            if (!CanNavigate(Frame.Source, Frame.Source, NavigationType.Refresh)) return;
            Navigate(Frame.Source, Frame.Source, NavigationType.Refresh);
        }


        private void SetContent<TK>(Uri newSource, NavigationType navigationType, object newContent, bool contentIsError, TK passingParameter)
        {
            // assign content
            Frame.Content = newContent;
            // do not raise navigated event when error
            if (!contentIsError)
            {
                var args = new ParameterNavigationEventArgs<TK>
                {
                    Frame = Frame,
                    Source = newSource,
                    Content = newContent,
                    NavigationType = navigationType,
                    Parameter = passingParameter
                };

                OnNavigated(Frame.Content, newContent, args);

                OnParameterNavigation(newContent, args);
            }
            // set IsLoadingContent to false
            Frame.SetValue(ModernFrame.IsLoadingContentPropertyKey, false);

            if (contentIsError) return;

            // and raise optional fragment navigation events
            string fragment;
            NavigationHelper.RemoveFragment(newSource, out fragment);

            if (fragment == null) return;

            // fragment navigation
            var fragmentArgs = new FragmentNavigationEventArgs
            {
                Fragment = fragment
            };

            OnFragmentNavigation(newContent, fragmentArgs);
        }

        /// <summary>
        /// Determines whether the specified content should be kept alive.
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        private bool ShouldKeepContentAlive(object content)
        {
            var dependencyObject = content as DependencyObject;

            if (dependencyObject == null) return Frame.KeepContentAlive;

            var result = GetKeepAlive(dependencyObject);

            // if a value exists for given content, use it
            return result ?? Frame.KeepContentAlive;
            // otherwise let the ModernFrame decide
        }

        /// <summary>
        /// Gets a value indicating whether to keep specified object alive in a ModernFrame instance.
        /// </summary>
        /// <param name="o">The target dependency object.</param>
        /// <returns>Whether to keep the object alive. Null to leave the decision to the ModernFrame.</returns>
        public static bool? GetKeepAlive(DependencyObject o)
        {
            if (o == null)
            {
                throw new ArgumentNullException("o");
            }
            return (bool?)o.GetValue(ModernFrame.KeepAliveProperty);
        }


        private void OnNavigating(object content, NavigatingCancelEventArgs e)
        {            
            e.IsParentFrameNavigating = e.Frame != Frame;
            // invoke IContent.OnNavigatingFrom on View     
            (content as IContent)?.OnNavigatingFrom(e);

            // invoke IContent.OnNavigatingFrom on View.
            var frameworkElement = content as FrameworkElement;
            (frameworkElement?.DataContext as IContent)?.OnNavigatingFrom(e);

            Navigating?.Invoke(this, e);
        }  

        private void OnNavigated(object oldContent, object newContent, NavigationEventArgs e)
        {
            // invoke IContent.OnNavigatedFrom and OnNavigatedTo on View
            (oldContent as IContent)?.OnNavigatedFrom(e);
            (newContent as IContent)?.OnNavigatedTo(e);
            // invoke IContent.OnNavigatedFrom and OnNavigatedTo on ViewModel            
            var frameworkElement = oldContent as FrameworkElement;
            (frameworkElement?.DataContext as IContent)?.OnNavigatedFrom(e);

            frameworkElement = newContent as FrameworkElement;
            (frameworkElement?.DataContext as IContent)?.OnNavigatedTo(e);

            Navigated?.Invoke(this, e);
        }

        private void OnFragmentNavigation(object content, FragmentNavigationEventArgs e)
        {
            // invoke optional IContent.OnFragmentNavigation on View.
            (content as IContent)?.OnFragmentNavigation(e);
            // invoke optional IContent.OnFragmentNavigation on ViewModel.
            var frameworkElement = content as FrameworkElement;
            (frameworkElement?.DataContext as IContent)?.OnFragmentNavigation(e);

            FragmentNavigation?.Invoke(this, e);
        }

        private void OnParameterNavigation<TK>(object newContent, ParameterNavigationEventArgs<TK> e)
        {
            (newContent as IContent)?.OnNavigatedTo(e);
            var frameworkElement = newContent as FrameworkElement;
            (frameworkElement?.DataContext as IContent)?.OnNavigatedTo(e);
        }

        private void OnNavigationFailed(NavigationFailedEventArgs e)
        {
            NavigationFailed?.Invoke(this, e);
        }
    }
}
