import logging
import winreg
from pathlib import Path

def set_wallpaper_style():
    """Set the desktop wallpaper style to 'fit'."""
    try:
        reg_key = r"Control Panel\Desktop"

        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, reg_key, 0, winreg.KEY_SET_VALUE) as key:
            # Set the wallpaper style to 'fit'
            winreg.SetValueEx(key, "WallpaperStyle", 0, winreg.REG_SZ, "6")
            winreg.SetValueEx(key, "TileWallpaper", 0, winreg.REG_SZ, "0")

        logging.info("Desktop wallpaper style set to 'fit'")
    except Exception as e:
        logging.error(f"Failed to set desktop wallpaper style: {e}")

def set_lock_screen_wallpaper(image_path):
    """Set the lock screen wallpaper by modifying the registry."""
    if not image_path:
        logging.error("Image path is empty.")
        return

    try:
        image_path_str = str(image_path)  # Convert Path object to string
        reg_key = r"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP"

        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, reg_key, 0, winreg.KEY_SET_VALUE) as key:
            # Set the LockScreenImagePath as a REG_SZ (string)
            winreg.SetValueEx(key, "LockScreenImagePath", 0, winreg.REG_SZ, image_path_str)
            # Set the LockScreenImageStatus as a REG_DWORD (integer)
            winreg.SetValueEx(key, "LockScreenImageStatus", 0, winreg.REG_DWORD, 1)

        logging.info(f"Lock screen wallpaper set to {image_path_str}")
    except Exception as e:
        logging.error(f"Failed to set lock screen wallpaper: {e}")

# this maybe doesnt work? i dont know. its made up by ai. 
def set_lock_screen_wallpaper_style():
    """Set the lock screen wallpaper style to 'fit'."""
    try:
        reg_key = r"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP"

        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, reg_key, 0, winreg.KEY_SET_VALUE) as key:
            # Ensure the lock screen image is set to 'fit'
            winreg.SetValueEx(key, "LockScreenImageFit", 0, winreg.REG_DWORD, 1)

        logging.info("Lock screen wallpaper style set to 'fit'")
    except Exception as e:
        logging.error(f"Failed to set lock screen wallpaper style: {e}")