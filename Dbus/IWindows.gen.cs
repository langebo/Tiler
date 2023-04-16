using System.Runtime.CompilerServices;
using Tmds.DBus;

[assembly: InternalsVisibleTo(Tmds.DBus.Connection.DynamicAssemblyName)]
namespace Tiler.DBus;

[DBusInterface("org.gnome.Shell.Extensions.Windows")]
interface IWindows : IDBusObject
{
    Task<string> ListAsync();
    Task<string> DetailsAsync(uint Winid);
    Task<string> GetTitleAsync(uint Winid);
    Task MoveToWorkspaceAsync(uint Winid, uint WorkspaceNum);
    Task MoveResizeAsync(uint Winid, int X, int Y, uint Width, uint Height);
    Task ResizeAsync(uint Winid, uint Width, uint Height);
    Task MoveAsync(uint Winid, int X, int Y);
    Task MaximizeAsync(uint Winid);
    Task MinimizeAsync(uint Winid);
    Task UnmaximizeAsync(uint Winid);
    Task UnminimizeAsync(uint Winid);
    Task ActivateAsync(uint Winid);
    Task CloseAsync(uint Winid);
}