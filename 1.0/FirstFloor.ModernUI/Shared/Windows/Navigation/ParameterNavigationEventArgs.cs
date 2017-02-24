using System;

namespace FirstFloor.ModernUI.Windows.Navigation
{
    /// <summary>
    /// Provides data for parameter navigation events.
    /// </summary>
    public class ParameterNavigationEventArgs<T> : NavigationEventArgs
    {
        /// <summary>
        /// Gets or sets the parameter.
        /// </summary>
        public T Parameter { get; set; }
    }
}
