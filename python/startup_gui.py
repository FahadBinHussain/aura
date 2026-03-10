import sys
import winreg as reg
import logging

def set_startup(enable):
    s_name = "WallpaperUpdater"
    exe_path = sys.executable

    key = reg.HKEY_CURRENT_USER
    key_value = r"Software\Microsoft\Windows\CurrentVersion\Run"

    if enable:
        open_key = reg.OpenKey(key, key_value, 0, reg.KEY_ALL_ACCESS)
        reg.SetValueEx(open_key, s_name, 0, reg.REG_SZ, exe_path)
        reg.CloseKey(open_key)
        logging.info("Added to startup")
    else:
        try:
            open_key = reg.OpenKey(key, key_value, 0, reg.KEY_ALL_ACCESS)
            reg.DeleteValue(open_key, s_name)
            reg.CloseKey(open_key)
            logging.info("Removed from startup")
        except FileNotFoundError:
            logging.info("Already removed from startup")

def is_startup_enabled():
    s_name = "WallpaperUpdater"
    key = reg.HKEY_CURRENT_USER
    key_value = r"Software\Microsoft\Windows\CurrentVersion\Run"
    try:
        open_key = reg.OpenKey(key, key_value, 0, reg.KEY_READ)
        value, _ = reg.QueryValueEx(open_key, s_name)
        reg.CloseKey(open_key)
        return value == sys.executable
    except FileNotFoundError:
        return False