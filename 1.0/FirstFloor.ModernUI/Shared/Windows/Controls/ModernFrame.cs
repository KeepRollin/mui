using FirstFloor.ModernUI.Windows.Media;
using FirstFloor.ModernUI.Windows.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FragmentNavigationEventArgs = FirstFloor.ModernUI.Windows.Navigation.FragmentNavigationEventArgs;
using NavigatingCancelEventArgs = FirstFloor.ModernUI.Windows.Navigation.NavigatingCancelEventArgs;
using NavigationEventArgs = FirstFloor.ModernUI.Windows.Navigation.NavigationEventArgs;
using NavigationFailedEventArgs = FirstFloor.ModernUI.Windows.Navigation.NavigationFailedEventArgs;

namespace FirstFloor.ModernUI.Windows.Controls
{
    /// <summary>
    /// A simple content frame implementation with navigation support.
    /// </summary>
    public class ModernFrame
        : ContentControl
    {
        /// <summary>
        /// Identifies the KeepAlive attached dependency property.
        /// </summary>
        public static readonly DependencyProperty KeepAliveProperty = DependencyProperty.RegisterAttached("KeepAlive", typeof(bool?), typeof(ModernFrame), new PropertyMetadata(null));
        /// <summary>
        /// Identifies the KeepContentAlive dependency property.
        /// </summary>
        public static readonly DependencyProperty KeepContentAliveProperty = DependencyProperty.Register("KeepContentAlive", typeof(bool), typeof(ModernFrame), new PropertyMetadata(true, OnKeepContentAliveChanged));
        /// <summary>
        /// Identifies the ContentLoader dependency property.
        /// </summary>
        public static readonly DependencyProperty ContentLoaderProperty = DependencyProperty.Register("ContentLoader", typeof(IContentLoader), typeof(ModernFrame), new PropertyMetadata(new DefaultContentLoader(), OnContentLoaderChanged));
        internal static readonly DependencyPropertyKey IsLoadingContentPropertyKey = DependencyProperty.RegisterReadOnly("IsLoadingContent", typeof(bool), typeof(ModernFrame), new PropertyMetadata(false));
        /// <summary>
        /// Identifies the IsLoadingContent dependency property.
        /// </summary>
        public static readonly DependencyProperty IsLoadingContentProperty = IsLoadingContentPropertyKey.DependencyProperty;
        /// <summary>
        /// Identifies the Source dependency property.
        /// </summary>
        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register("Source", typeof(Uri), typeof(ModernFrame), new PropertyMetadata(OnSourceChanged));
                
        private Dictionary<Uri, object> contentCache = new Dictionary<Uri, object>();
#if NET4
        private List<WeakReference> childFrames = new List<WeakReference>();        // list of registered frames in sub tree
#else
        private List<WeakReference<ModernFrame>> childFrames = new List<WeakReference<ModernFrame>>();        // list of registered frames in sub tree
#endif
        internal CancellationTokenSource tokenSource;
        internal bool isNavigatingHistory;
        internal bool isResetSource;

        private IModernNavigationService<ModernFrame> _navigationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ModernFrame"/> class.
        /// </summary>
        public ModernFrame()
        {
            this.DefaultStyleKey = typeof(ModernFrame);

            // associate application and navigation commands with this instance
            this.CommandBindings.Add(new CommandBinding(NavigationCommands.BrowseBack, OnBrowseBack, OnCanBrowseBack));
            this.CommandBindings.Add(new CommandBinding(NavigationCommands.GoToPage, OnGoToPage, OnCanGoToPage));
            this.CommandBindings.Add(new CommandBinding(NavigationCommands.Refresh, OnRefresh, OnCanRefresh));
            this.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, OnCopy, OnCanCopy));

            this.Loaded += OnLoaded;            
        }

        private static void OnKeepContentAliveChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            ((ModernFrame)o).OnKeepContentAliveChanged((bool)e.NewValue);
        }

        private void OnKeepContentAliveChanged(bool keepAlive)
        {
            // clear content cache
            this.contentCache.Clear();
        }

        private static void OnContentLoaderChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue == null) {
                // null values for content loader not allowed
                throw new ArgumentNullException("ContentLoader");
            }
        }

        private static void OnSourceChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            ((ModernFrame)o).OnSourceChanged((Uri)e.OldValue, (Uri)e.NewValue);
        }

        private void OnSourceChanged(Uri oldValue, Uri newValue)
        {
            // if resetting source or old source equals new, don't do anything
            if (this.isResetSource || newValue != null && newValue.Equals(oldValue)) {
                return;
            }

            // handle fragment navigation
            string newFragment = null;
            var oldValueNoFragment = NavigationHelper.RemoveFragment(oldValue);
            var newValueNoFragment = NavigationHelper.RemoveFragment(newValue, out newFragment);

            if (newValueNoFragment != null && newValueNoFragment.Equals(oldValueNoFragment)) {
                // fragment navigation
                var args = new FragmentNavigationEventArgs {
                    Fragment = newFragment
                };

                OnFragmentNavigation(this.Content as IContent, args);
            }
            else {
                var navType = this.isNavigatingHistory ? NavigationType.Back : NavigationType.New;

                // only invoke CanNavigate for new navigation
                if (!this.isNavigatingHistory && !NavigationService.CanNavigate(oldValue, newValue, navType)) {
                    return;
                }

                this.NavigationService.Navigate(oldValue, newValue, navType);
            }
        }       

        private IEnumerable<ModernFrame> GetChildFrames()
        {
            var refs = this.childFrames.ToArray();
            foreach (var r in refs) {
                var valid = false;
                ModernFrame frame;

#if NET4
                if (r.IsAlive) {
                    frame = (ModernFrame)r.Target;
#else
                if (r.TryGetTarget(out frame)) {
#endif
                    // check if frame is still an actual child (not the case when child is removed, but not yet garbage collected)
                    if (NavigationHelper.FindFrame(null, frame) == this) {
                        valid = true;
                        yield return frame;
                    }
                }

                if (!valid) {
                    this.childFrames.Remove(r);
                }
            }
        }

        private void OnFragmentNavigation(IContent content, FragmentNavigationEventArgs e)
        {
            // invoke optional IContent.OnFragmentNavigation
            if (content != null) {
                content.OnFragmentNavigation(e);
            }

            ((content as Control)?.DataContext as IContent)?.OnFragmentNavigation(e);            
        }
       
        /// <summary>
        /// Determines whether the routed event args should be handled.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        /// <remarks>This method prevents parent frames from handling routed commands.</remarks>
        private bool HandleRoutedEvent(CanExecuteRoutedEventArgs args)
        {
            var originalSource = args.OriginalSource as DependencyObject;

            if (originalSource == null) {
                return false;
            }
            return originalSource.AncestorsAndSelf().OfType<ModernFrame>().FirstOrDefault() == this;
        }

        private void OnCanBrowseBack(object sender, CanExecuteRoutedEventArgs e)
        {
            // only enable browse back for source frame, do not bubble
            if (HandleRoutedEvent(e))
            {
                e.CanExecute = NavigationService.CanBrowseBack();
            }
        }

        private void OnCanCopy(object sender, CanExecuteRoutedEventArgs e)
        {
            if (HandleRoutedEvent(e)) {
                e.CanExecute = this.Content != null;
            }
        }

        private void OnCanGoToPage(object sender, CanExecuteRoutedEventArgs e)
        {
            if (HandleRoutedEvent(e)) {
                e.CanExecute = e.Parameter is String || e.Parameter is Uri;
            }
        }

        private void OnCanRefresh(object sender, CanExecuteRoutedEventArgs e)
        {
            if (HandleRoutedEvent(e)) {
                e.CanExecute = this.Source != null;
            }
        }

        private void OnBrowseBack(object target, ExecutedRoutedEventArgs e)
        {
            NavigationService.BrowseBack();
        }

        private void OnGoToPage(object target, ExecutedRoutedEventArgs e)
        {
            var newValue = NavigationHelper.ToUri(e.Parameter);
            SetCurrentValue(SourceProperty, newValue);
        }

        private void OnRefresh(object target, ExecutedRoutedEventArgs e)
        {
            if (NavigationService.CanNavigate(this.Source, this.Source, NavigationType.Refresh)) {
                NavigationService.Navigate(this.Source, this.Source, NavigationType.Refresh);
            }
        }

        private void OnCopy(object target, ExecutedRoutedEventArgs e)
        {
            // copies the string representation of the current content to the clipboard
            Clipboard.SetText(this.Content.ToString());
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var parent = NavigationHelper.FindFrame(NavigationHelper.FrameParent, this);
            if (parent != null) {
                parent.RegisterChildFrame(this);
            }
        }

        private void RegisterChildFrame(ModernFrame frame)
        {
            // do not register existing frame
            if (!GetChildFrames().Contains(frame)) {
#if NET4
                var r = new WeakReference(frame);
#else
                var r = new WeakReference<ModernFrame>(frame);
#endif
                this.childFrames.Add(r);
            }
        }

        /// <summary>
        /// Determines whether the specified content should be kept alive.
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        private bool ShouldKeepContentAlive(object content)
        {
            var o = content as DependencyObject;
            if (o != null) {
                var result = GetKeepAlive(o);

                // if a value exists for given content, use it
                if (result.HasValue) {
                    return result.Value;
                }
            }
            // otherwise let the ModernFrame decide
            return this.KeepContentAlive;
        }

        /// <summary>
        /// Gets a value indicating whether to keep specified object alive in a ModernFrame instance.
        /// </summary>
        /// <param name="o">The target dependency object.</param>
        /// <returns>Whether to keep the object alive. Null to leave the decision to the ModernFrame.</returns>
        public static bool? GetKeepAlive(DependencyObject o)
        {
            if (o == null) {
                throw new ArgumentNullException("o");
            }
            return (bool?)o.GetValue(KeepAliveProperty);
        }

        /// <summary>
        /// Sets a value indicating whether to keep specified object alive in a ModernFrame instance.
        /// </summary>
        /// <param name="o">The target dependency object.</param>
        /// <param name="value">Whether to keep the object alive. Null to leave the decision to the ModernFrame.</param>
        public static void SetKeepAlive(DependencyObject o, bool? value)
        {
            if (o == null) {
                throw new ArgumentNullException("o");
            }
            o.SetValue(KeepAliveProperty, value);
        }

        /// <summary>
        /// Gets or sets a value whether content should be kept in memory.
        /// </summary>
        public bool KeepContentAlive
        {
            get { return (bool)GetValue(KeepContentAliveProperty); }
            set { SetValue(KeepContentAliveProperty, value); }
        }

        /// <summary>
        /// Gets or sets the content loader.
        /// </summary>
        public IContentLoader ContentLoader
        {
            get { return (IContentLoader)GetValue(ContentLoaderProperty); }
            set { SetValue(ContentLoaderProperty, value); }
        }

        /// <summary>
        /// Gets or sets the navigation service.
        /// </summary>
        public IModernNavigationService<ModernFrame> NavigationService
        {
            get { return _navigationService ?? (_navigationService = new DefaultNavigationService(this)); }
            set { _navigationService = value; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance is currently loading content.
        /// </summary>
        public bool IsLoadingContent
        {
            get { return (bool)GetValue(IsLoadingContentProperty); }
        }

        /// <summary>
        /// Gets or sets the source of the current content.
        /// </summary>
        public Uri Source
        {
            get { return (Uri)GetValue(SourceProperty); }
            set { SetValue(SourceProperty, value); }
        }
    }
}
