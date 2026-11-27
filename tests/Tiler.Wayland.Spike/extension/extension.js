// Spike prototype (issue #6): the smallest session-D-Bus service that can tile a window.
// Throwaway — TL-301 authors the shipping `src/Tiler.Helper/extension.js` from what this proves.

import Gio from 'gi://Gio';

const BUS_NAME = 'dev.blange.Tiler';
const OBJECT_PATH = '/dev/blange/Tiler';

const DBUS_IFACE = `
<node>
   <interface name="dev.blange.Tiler">
      <method name="GetFocused">
         <arg type="u" direction="out" name="winid" />
         <arg type="s" direction="out" name="title" />
      </method>
      <method name="GetWorkarea">
         <arg type="s" direction="out" name="workarea" />
      </method>
      <method name="MoveResize">
         <arg type="u" direction="in" name="winid" />
         <arg type="i" direction="in" name="x" />
         <arg type="i" direction="in" name="y" />
         <arg type="u" direction="in" name="width" />
         <arg type="u" direction="in" name="height" />
      </method>
   </interface>
</node>`;

export default class Extension {
    enable() {
        this._dbus = Gio.DBusExportedObject.wrapJSObject(DBUS_IFACE, this);
        this._dbus.export(Gio.DBus.session, OBJECT_PATH);
        // wrapJSObject.export only puts the object on the connection tree; the well-known
        // name has to be claimed separately (the shell already owns org.gnome.Shell, which is
        // why window-calls has no such call).
        this._ownerId = Gio.bus_own_name(Gio.BusType.SESSION, BUS_NAME,
            Gio.BusNameOwnerFlags.NONE, null, null, null);
    }

    disable() {
        Gio.bus_unown_name(this._ownerId);
        this._dbus.flush();
        this._dbus.unexport();
        delete this._dbus;
    }

    _window_by_id(winid) {
        const actor = global.get_window_actors().find(a => a.meta_window.get_id() === winid);
        if (!actor)
            throw new Error(`window ${winid} not found`);
        return actor.meta_window;
    }

    GetFocused() {
        const actor = global.get_window_actors().find(a => a.meta_window.has_focus());
        if (!actor)
            return [0, ''];
        return [actor.meta_window.get_id(), actor.meta_window.get_title() ?? ''];
    }

    // Spike-only probe for the shipping GetMonitors: the focused window's monitor work area.
    GetWorkarea() {
        const actor = global.get_window_actors().find(a => a.meta_window.has_focus());
        if (!actor)
            throw new Error('no focused window');
        const monitor = global.display.get_current_monitor();
        const area = actor.meta_window.get_work_area_for_monitor(monitor);
        return JSON.stringify({ x: area.x, y: area.y, w: area.width, h: area.height });
    }

    MoveResize(winid, x, y, width, height) {
        const win = this._window_by_id(winid);
        // move_resize_frame is a no-op on a maximized window; unmaximize(3) clears both axes.
        if (win.maximized_horizontally || win.maximized_vertically)
            win.unmaximize(3);
        win.move_resize_frame(1, x, y, width, height);
    }
}
