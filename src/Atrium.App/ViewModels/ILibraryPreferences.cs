using System.ComponentModel;
using Atrium.Core.Models;

namespace Atrium.App.ViewModels;

public interface ILibraryPreferences : INotifyPropertyChanged
{
    StoreKind PreferredDefaultStore { get; }
}
