using System;
using System.Windows.Input;
using FirstFloor.ModernUI.Windows.Controls;

namespace FirstFloor.ModernUI.Windows.Navigation
{
    /// <summary>
    /// Interface for navigation against ModernFrame.
    /// </summary>    
    public interface IModernNavigationService<T> where T: ModernFrame
    {

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        bool CanBrowseBack();
        /// <summary>
        /// 
        /// </summary>        
        void BrowseBack();       
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
        bool CanNavigate(Uri oldValue, Uri newValue, NavigationType navigationType);
        /// <summary>
        /// Performs navigation against current ModernFrame.
        /// </summary>        
        /// <param name="oldValue">Navigate From Uri</param>
        /// <param name="newValue">Navigate To Uri</param>
        /// <param name="navigationType">Type of Navigation</param>
        /// <remarks>
        /// Can be used to fire Navigated events.
        /// </remarks>
        void Navigate(Uri oldValue, Uri newValue, NavigationType navigationType);
        /// <summary>
        /// The reference of current ModernFrame on which navigation is performed.
        /// </summary>
        T Frame { get; }
        /// <summary>
        /// Occurs when navigation to a content fragment begins.
        /// </summary>
        event EventHandler<FragmentNavigationEventArgs> FragmentNavigation;
        /// <summary>
        /// Occurs when a new navigation is requested.
        /// </summary>
        /// <remarks>
        /// The navigating event is also raised when a parent frame is navigating. This allows for cancelling parent navigation.
        /// </remarks>
        event EventHandler<NavigatingCancelEventArgs> Navigating;
        /// <summary>
        /// Occurs when navigation to new content has completed.
        /// </summary>
        event EventHandler<NavigationEventArgs> Navigated;
        /// <summary>
        /// Occurs when navigation has failed.
        /// </summary>
        event EventHandler<NavigationFailedEventArgs> NavigationFailed;
    }
}