import time
import requests
import subprocess
import json
from pathlib import Path
from bs4 import BeautifulSoup
import random
import logging
import psutil
import os
from python.utils import load_env_vars, load_config  # Import utility functions
import sys

# Setup logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')

# Load environment variables
load_env_vars()

# Load configuration fresh each time
config = load_config()

# Wallpaper Engine related functions

def printlog(log):
    """Log messages to the console."""
    print(log)
    logging.info(log)

def fetch_page_content(url, stop_event):
    """Fetch a webpage's content."""
    try:
        if stop_event and stop_event.is_set():
            return None
            
        response = requests.get(url, timeout=10)
        response.raise_for_status()
        return response.text
    except requests.RequestException as e:
        if stop_event and stop_event.is_set():  # Check first if we should abort
            return None
        logging.warning(f"Failed to fetch {url}: {e}")
        return None

def get_random_credential_pair():
    """Select a random username-password pair."""
    usernames = os.getenv('USERNAMES').split(',')
    passwords = os.getenv('PASSWORDS').split(',')
    
    if not usernames or not passwords:
        logging.error("Usernames or passwords are not set in environment variables.")
        return None, None
    
    index = random.randint(0, len(usernames) - 1)
    return usernames[index], passwords[index]

def log_downloaded_wallpaper(pubfileid):
    """Log downloaded wallpaper metadata."""
    save_location = Path(config['SAVE_LOCATION'])
    history_file = save_location / "wallpaper_history.json"
    try:
        history = {}
        if history_file.exists():
            with history_file.open("r") as f:
                history = json.load(f)
        history[pubfileid] = {
            "timestamp": time.time(),
            "path": str(save_location / "projects" / "myprojects" / pubfileid)
        }
        with history_file.open("w") as f:
            json.dump(history, f, indent=4)
    except (json.JSONDecodeError, IOError) as e:
        logging.error(f"Error logging wallpaper: {e}")

# def validate_we_path():
#     """Validate Wallpaper Engine installation path."""
#     config = load_config()
#     required_files = ["wallpaper32.exe", "wallpaper64.exe"]
#     path = Path(config['SAVE_LOCATION'])
#     if not path.exists():
#         logging.error(f"Wallpaper Engine path not found: {path}")
#         return False
#     return all((path / file).exists() for file in required_files)

def set_downloaded_wallpaper(wallpaper_path):
    """Set the downloaded wallpaper using Wallpaper Engine's CLI."""
    config = load_config()
    we_path = Path(config['SAVE_LOCATION'])
    
    # Try different executable variants
    exe_names = ["wallpaper64.exe", "wallpaper32.exe"]
    we_exe = next((we_path / exe for exe in exe_names if (we_path / exe).exists()), None)
    
    if not we_exe:
        logging.error("No valid Wallpaper Engine executable found")
        return False

    try:
        # Check if PKG exists, fallback to MP4
        wp_path = Path(wallpaper_path)
        if not wp_path.exists():
            # Look for MP4 files in the same directory
            mp4_files = list(wp_path.parent.glob("*.mp4"))
            if not mp4_files:
                logging.error(f"No wallpaper files found in {wp_path.parent}")
                return False
            wallpaper_path = str(mp4_files[0])
            logging.info(f"Using MP4 wallpaper: {wallpaper_path}")

        command = [
            str(we_exe),
            "-control", "openWallpaper",
            "-file", str(wallpaper_path),
            "play"
        ]
        process = subprocess.Popen(
            command,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            creationflags=subprocess.CREATE_NO_WINDOW | subprocess.CREATE_BREAKAWAY_FROM_JOB
        )
        for line in process.stdout:
            logging.info(f"[Wallpaper Engine] {line.strip()}")
        time.sleep(10)  # Add safety delay before cleanup
        return True
    except (subprocess.SubprocessError, FileNotFoundError) as e:
        logging.error(f"Failed to set wallpaper: {e}")
        return False

# def should_perform_scrape():
#     """Determine if scraping should be performed based on interval."""
#     try:
#         last_scrape = Path("last_scrape_time.txt").stat().st_mtime
#         return (time.time() - last_scrape) > int(config['SCRAPE_INTERVAL'])
#     except (FileNotFoundError, OSError):
#         return True

# def update_last_scrape_time():
#     """Update the last scrape timestamp."""
#     with open("last_scrape_time.txt", "w") as f:
#         f.write(str(time.time()))

def clean_and_filter_wallpaper_links(links):
    """Filter and clean wallpaper links to ensure they match the required format."""
    filtered_links = [link.split("&")[0] for link in links if "steamcommunity.com/sharedfiles/filedetails/?id=" in link]
    logging.info(f"Filtered {len(filtered_links)} valid wallpaper links.")
    return filtered_links

def scrape_wallpapers(stop_event):
    """Scrape wallpapers and return unique links."""
    random_page = random.randint(1, 1000)
    config = load_config()
    collections_url = config['COLLECTIONS_URL'].format(page=random_page)
    logging.info(f"Fetching wallpaper links from page {random_page}...")

    wallpapers_page = fetch_page_content(collections_url, stop_event)
    if wallpapers_page:
        logging.info("Parsing wallpapers...")
        soup = BeautifulSoup(wallpapers_page, 'html.parser')
        raw_links = [a['href'] for a in soup.select('div.workshopItem a') if a.get('href')]

        wallpaper_links = clean_and_filter_wallpaper_links(raw_links)
        unique_links = set(wallpaper_links)
        logging.info(f"Found {len(unique_links)} unique wallpapers on page {random_page}.")
        return list(unique_links)
    else:
        logging.warning(f"Failed to fetch or parse page {random_page}.")
    return []

def download_random_wallpapers(wallpaper_links, stop_event=None):
    """Download random wallpapers using depotdownloader."""
    try:
        config = load_config()
        if stop_event and stop_event.is_set():
            return

        wallpaper_download_limit = int(config['WALLPAPER_DOWNLOAD_LIMIT'])
        if len(wallpaper_links) < wallpaper_download_limit:
            logging.warning("Not enough wallpapers to download.")
            return

        selected_links = random.sample(wallpaper_links, wallpaper_download_limit)
        
        base_path = os.path.dirname(sys.executable) if getattr(sys, 'frozen', False) else os.path.dirname(__file__)
        depot_path = os.path.join(base_path, "DepotDownloaderMod", "DepotDownloadermod.exe")
        
        # For EXE builds, check one level up if needed
        if not os.path.exists(depot_path) and getattr(sys, 'frozen', False):
            exe_parent = Path(sys.executable).parent
            depot_path = exe_parent.parent / "DepotDownloaderMod" / "DepotDownloadermod.exe"
        
        logging.info(f"Resolved depotdownloader path: {depot_path}")
        
        if not os.path.exists(depot_path):
            logging.error(f"DepotDownloader not found at: {depot_path}")
            return

        for link in selected_links:
            if stop_event and stop_event.is_set():
                return
                
            pubfileid = link.split("id=")[1]
            logging.info(f"Downloading wallpaper ID {pubfileid}")
            
            # Create specific directory for this wallpaper
            save_location = Path(config['SAVE_LOCATION'])
            directory = save_location / "projects" / "myprojects" / pubfileid
            directory.mkdir(parents=True, exist_ok=True)

            username, password = get_random_credential_pair()
            if not username or not password:
                logging.error("Invalid credentials, skipping download")
                continue

            try:
                process = subprocess.Popen(
                    [
                        depot_path,
                        "-app", "431960",
                        "-pubfile", pubfileid,
                        "-verify-all",
                        "-username", username,
                        "-password", password,
                        "-dir", str(directory)
                    ],
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    text=True,
                    creationflags=subprocess.CREATE_NO_WINDOW | subprocess.CREATE_BREAKAWAY_FROM_JOB
                )
                for line in process.stdout:
                    logging.info(f"[DepotDownloader] {line.strip()}")
                
                log_downloaded_wallpaper(pubfileid)
                wallpaper_path = directory / "scene.pkg"
                set_downloaded_wallpaper(str(wallpaper_path))
                time.sleep(10)  # Add safety delay before cleanup
                
                if stop_event and stop_event.is_set():
                    process.terminate()
                    return
                
            except subprocess.CalledProcessError as e:
                logging.error(f"Download failed for {pubfileid}: {e}")

    except Exception as e:
        logging.error(f"Error in download process: {e}")

def close_wallpaper_engine():
    """Close Wallpaper Engine if it is running."""
    for process in psutil.process_iter(attrs=["name"]):
        if process.info["name"] == "wallpaper32.exe" or process.info["name"] == "wallpaper64.exe":
            logging.info(f"Terminating {process.info['name']} (PID: {process.pid})")
            process.terminate()
            process.wait()  # Wait for the process to be terminated
            logging.info(f"{process.info['name']} terminated.")
            return

def automate_wallpaper_update(stop_event=None):
    """Automate the process of scraping, downloading, and setting wallpapers."""
    logging.info("Starting automated wallpaper update.")
    wallpaper_links = scrape_wallpapers(stop_event)

    if wallpaper_links:
        download_random_wallpapers(wallpaper_links, stop_event)
    else:
        logging.warning("No new wallpapers to download.")
    logging.info("Completed automated wallpaper update.")

# Example usage
# if __name__ == "__main__":
#     automate_wallpaper_update()