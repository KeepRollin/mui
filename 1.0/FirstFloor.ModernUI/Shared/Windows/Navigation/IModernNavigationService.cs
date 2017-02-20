using System;
using System.Windows.Controls;
using FirstFloor.ModernUI.Windows.Navigation;

namespace FirstFloor.ModernUI.Windows.Controls
{
    /// <summary>
    /// 
    /// </summary>    
    public interface IModernNavigationService
    {
        /// <summary>
        /// 
        /// </summary>       
        /// <param name="oldValue"></param>
        /// <param name="newValue"></param>
        /// <param name="navigationType"></param>
        /// <returns></returns>
        bool CanNavigate(Uri oldValue, Uri newValue, NavigationType navigationType);
        /// <summary>
        /// 
        /// </summary>        
        /// <param name="oldValue"></param>
        /// <param name="newValue"></param>
        /// <param name="navigationType"></param>
        void Navigate(Uri oldValue, Uri newValue, NavigationType navigationType);
    }
}