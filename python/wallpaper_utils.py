import logging
import os
import shutil
import subprocess
import time

def get_latest_wallpaper(directory):
    """Get the latest wallpaper file from the specified directory."""
    wallpapers = list(directory.glob("*.jpg"))
    if not wallpapers:
        return None
    latest_wallpaper = max(wallpapers, key=os.path.getctime)
    logging.info(f"Latest wallpaper identified: {latest_wallpaper}")
    return latest_wallpaper

def terminate_depotdownloader():
    """Terminate DepotDownloaderMod.exe if it is running."""
    try:
        # Check if process exists before killing
        result = subprocess.run(["tasklist", "/fi", "imagename eq DepotDownloaderMod.exe"], 
                              capture_output=True, text=True)
        if "DepotDownloaderMod.exe" in result.stdout:
            subprocess.run(["taskkill", "/f", "/im", "DepotDownloaderMod.exe"], 
                          check=True, creationflags=subprocess.CREATE_NO_WINDOW)
            logging.info("Successfully terminated DepotDownloaderMod.exe")
    except subprocess.CalledProcessError as e:
        logging.error(f"Failed to terminate DepotDownloaderMod.exe: {e}")

def cleanup_old_wallpapers(directory, max_wallpapers):
    """Delete old wallpapers to save space."""
    wallpaper_files = list(directory.glob("*.jpg"))

    if len(wallpaper_files) > max_wallpapers:
        wallpaper_files.sort(key=os.path.getctime)
        
        for file_to_delete in wallpaper_files[:-max_wallpapers]:
            logging.info(f"Deleting old wallpaper file: {file_to_delete}")
            try:
                file_to_delete.unlink()
                logging.info(f"Successfully deleted: {file_to_delete}")
            except OSError as e:
                logging.error(f"Failed to delete old wallpaper {file_to_delete}: {e}")

    target_dir = directory / "projects" / "myprojects"
    if target_dir.exists() and target_dir.is_dir():
        wallpaper_dirs = [d for d in target_dir.iterdir() if d.is_dir()]
        if len(wallpaper_dirs) > max_wallpapers:
            wallpaper_dirs.sort(key=os.path.getctime)
            for dir_to_delete in wallpaper_dirs[:len(wallpaper_dirs) - max_wallpapers]:
                # logging.info(f"Deleting old wallpaper directory: {dir_to_delete}")
                logging.info(f"Deleting old wallpaper directory")
                try:
                    for root, dirs, files in os.walk(dir_to_delete):
                        for file in files:
                            file_path = os.path.join(root, file)
                            os.chmod(file_path, 0o777)
                    shutil.rmtree(dir_to_delete)
                    # logging.info(f"Successfully deleted: {dir_to_delete}")
                    logging.info(f"Successfully deleted")
                except PermissionError as e:
                    logging.warning(f"Retrying cleanup after error: {e}")
                    time.sleep(2)
                    # Retry logic here
                except Exception as e:
                    logging.error(f"Unexpected error while deleting {dir_to_delete}: {e}")
                    