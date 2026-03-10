import requests
import logging
import shutil
from pathlib import Path
import os
import platform
import subprocess
import ctypes
import random
from python.utils import load_env_vars, load_config

# Load environment variables
load_env_vars()

# Load configuration
config = load_config()

# Unsplash API key from environment variable
UNSPLASH_ACCESS_KEY = os.getenv("UNSPLASH_ACCESS_KEY")

# Check if API key is loaded
if not UNSPLASH_ACCESS_KEY:
    logging.error("Unsplash Access Key is missing! Please set it in the .env file.")

def fetch_unsplash_wallpapers(query="wallpapers", count=10):
    """Fetch wallpapers from Unsplash API."""
    random_seed = random.randint(0, 10000)  # Add a random seed to ensure different results
    url = f"https://api.unsplash.com/photos/random?count={count}&query={query}&client_id={UNSPLASH_ACCESS_KEY}&random_seed={random_seed}"
    try:
        response = requests.get(url)
        response.raise_for_status()
        photos = response.json()
        wallpapers = [{"id": photo["id"], "username": photo["user"]["username"], "url": photo["urls"]["full"]} for photo in photos]
        logging.info(f"Fetched {len(wallpapers)} wallpapers from Unsplash.")
        return wallpapers
    except requests.RequestException as e:
        logging.error(f"Failed to fetch from Unsplash: {e}")
        return []

def save_unsplash_wallpapers(wallpapers, directory):
    """Save wallpapers from URLs to the specified directory."""
    directory.mkdir(parents=True, exist_ok=True)
    
    for wallpaper in wallpapers:
        try:
            response = requests.get(wallpaper["url"], stream=True)
            response.raise_for_status()
            file_path = directory / f"{wallpaper['id']}_{wallpaper['username']}.jpg"
            with open(file_path, "wb") as f:
                shutil.copyfileobj(response.raw, f)
            logging.info(f"Saved wallpaper to {file_path}")
        except requests.RequestException as e:
            logging.error(f"Failed to download wallpaper from {wallpaper['url']}: {e}")

def set_unsplash_wallpaper(file_path):
    """Set the wallpaper using a given file path depending on the OS."""
    system = platform.system()
    if system == "Windows":
        set_wallpaper_windows(file_path)
    elif system == "Darwin":  # macOS
        set_wallpaper_macos(file_path)
    elif system == "Linux":
        set_wallpaper_linux(file_path)
    else:
        logging.error(f"Unsupported operating system: {system}")

def set_wallpaper_windows(file_path):
    """Set the desktop wallpaper on Windows."""
    absolute_path = os.path.abspath(file_path)
    ctypes.windll.user32.SystemParametersInfoW(20, 0, absolute_path, 3)
    logging.info(f"Wallpaper set to {absolute_path} on Windows")

def set_wallpaper_macos(file_path):
    """Set the desktop wallpaper on macOS."""
    script = f'''
    tell application "System Events"
        set picture of every desktop to "{file_path}"
    end tell
    '''
    subprocess.run(["osascript", "-e", script])
    logging.info(f"Wallpaper set to {file_path} on macOS")

def set_wallpaper_linux(file_path):
    """Set the desktop wallpaper on Linux with GNOME."""
    subprocess.run(["gsettings", "set", "org.gnome.desktop.background", "picture-uri", f"file://{file_path}"])
    logging.info(f"Wallpaper set to {file_path} on Linux")

# Example usage:
# if __name__ == "__main__":
#     wallpapers = fetch_unsplash_wallpapers()
#     save_wallpapers(wallpapers, Path(config['SAVE_LOCATION']) / "unsplash_wallpapers")
#     latest_wallpaper = get_latest_wallpaper(Path(config['SAVE_LOCATION']) / "unsplash_wallpapers")
#     if latest_wallpaper:
#         set_wallpaper(latest_wallpaper)