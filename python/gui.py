import tkinter as tk
from tkinter import messagebox, scrolledtext
from tkinter import ttk
import threading
import random
import logging
import os
import time
import sys
from python.utils import load_env_vars, load_config, save_config
from pathlib import Path
from python.startup_gui import set_startup, is_startup_enabled
from python.registry_utils import set_wallpaper_style, set_lock_screen_wallpaper, set_lock_screen_wallpaper_style
import queue
from python.unsplash import fetch_unsplash_wallpapers, save_unsplash_wallpapers, set_unsplash_wallpaper
from python.pexels import fetch_pexels_wallpapers, save_pexels_wallpapers, set_pexels_wallpaper
from python.wallpaper_engine import automate_wallpaper_update, close_wallpaper_engine
from python.wallpaper_utils import get_latest_wallpaper, terminate_depotdownloader, cleanup_old_wallpapers

# Add this at the very start of the file (before config loading)
if getattr(sys, 'frozen', False):
    os.chdir(os.path.dirname(sys.executable))

class TextHandler(logging.Handler):
    """Class to handle logging messages and display them in a Tkinter Text widget."""
    
    def __init__(self, text_widget):
        logging.Handler.__init__(self)
        self.text_widget = text_widget
        self.setFormatter(logging.Formatter('%(asctime)s - %(levelname)s - %(message)s'))
        self.text_widget.config(state="disabled")  # Prevent accidental edits
        
    def emit(self, record):
        msg = self.format(record)
        self.text_widget.after(0, self._update_display, msg)  # Use main thread queue
        
    def _update_display(self, msg):
        self.text_widget.configure(state='normal')
        self.text_widget.insert(tk.END, msg + '\n')
        self.text_widget.configure(state='disabled')
        self.text_widget.yview(tk.END)

# Global variables
stop_event = threading.Event()
selected_sources = []
update_thread = None
log_queue = queue.Queue()

# BEFORE creating any GUI elements, load config
config = load_config()

# Initialize Tkinter root window
root = tk.Tk()
root.title("Wall-You-Need")
root.geometry("800x600")
root.resizable(True, True)

def start_wallpaper_update():
    """Main wallpaper update logic merged from main.py"""
    global selected_sources, config
    logging.info("Wallpaper update thread started")
    
    while not stop_event.is_set():
        try:
            current_sources = selected_sources.copy()
            save_location_path = Path(config['SAVE_LOCATION'])
            
            if not current_sources:
                logging.warning("No sources selected in current iteration")
                time.sleep(5)
                continue

            source = random.choice(current_sources)
            logging.info(f"Randomly chosen source: {source} from {current_sources}")

            if source == "unsplash":
                handle_unsplash_update(save_location_path)
            elif source == "pexels":
                handle_pexels_update(save_location_path)
            elif source == "wallpaper_engine":
                handle_wallpaper_engine_update(save_location_path)

            interval = int(config['CHECK_INTERVAL'])
            logging.info(f"Sleeping for {interval} seconds before the next update.")
            stop_event.wait(interval)
            
        except Exception as e:
            logging.error(f"Exception in update thread: {e}", exc_info=True)
            time.sleep(5)

def handle_unsplash_update(save_path):
    try:
        wallpapers = fetch_unsplash_wallpapers(query="landscape", count=1)
        if not wallpapers or stop_event.is_set():
            return
            
        save_unsplash_wallpapers(wallpapers, save_path / "unsplash_wallpapers")
        unsplash_wallpaper_path = get_latest_wallpaper(save_path / "unsplash_wallpapers")
        
        if unsplash_wallpaper_path and not stop_event.is_set():
            set_unsplash_wallpaper(unsplash_wallpaper_path)
            set_wallpaper_style()
            set_lock_screen_wallpaper(unsplash_wallpaper_path)
            close_wallpaper_engine()
            
        cleanup_old_wallpapers(save_path / "unsplash_wallpapers", 
                            int(config['MAX_WALLPAPERS']))
    except Exception as e:
        logging.error(f"Unsplash failed: {str(e)}", exc_info=True)

def handle_pexels_update(save_path):
    try:
        pexels_wallpapers = fetch_pexels_wallpapers(query="nature", count=1)
        if not pexels_wallpapers or stop_event.is_set():
            return
            
        save_pexels_wallpapers(pexels_wallpapers, save_path / "pexels_wallpapers")
        pexels_wallpaper_path = get_latest_wallpaper(save_path / "pexels_wallpapers")
        
        if pexels_wallpaper_path and not stop_event.is_set():
            set_pexels_wallpaper(pexels_wallpaper_path)
            set_wallpaper_style()
            set_lock_screen_wallpaper(pexels_wallpaper_path)
            close_wallpaper_engine()
            
        cleanup_old_wallpapers(save_path / "pexels_wallpapers", 
                             int(config['MAX_WALLPAPERS']))
    except Exception as e:
        logging.error(f"Pexels failed: {str(e)}", exc_info=True)

def handle_wallpaper_engine_update(save_path):
    try:
        logging.info("Initiating Wallpaper Engine download sequence")
        automate_wallpaper_update(stop_event=stop_event)
        terminate_depotdownloader()
        time.sleep(10)  # Add buffer before cleanup
        cleanup_old_wallpapers(save_path, int(config['MAX_WALLPAPERS']))
    except Exception as e:
        logging.error(f"Wallpaper Engine failed: {str(e)}", exc_info=True)

def update_config_file():
    global config
    # Update the in-memory config directly
    config['SAVE_LOCATION'] = save_location_var.get()
    config['CHECK_INTERVAL'] = check_interval_var.get()
    config['COLLECTIONS_URL'] = collections_url_var.get()
    config['WALLPAPER_DOWNLOAD_LIMIT'] = wallpaper_limit_var.get()
    config['MAX_WALLPAPERS'] = max_wallpapers_var.get()
    config['SAVE_OLD_WALLPAPERS'] = var_save_old_wallpapers.get()
    
    # Save source states
    config['SOURCE_UNSPLASH'] = var_unsplash.get()
    config['SOURCE_PEXELS'] = var_pexels.get()
    config['SOURCE_WALLPAPER_ENGINE'] = var_wallpaper_engine.get()
    
    # Update running state
    config['UPDATE_RUNNING'] = update_thread is not None and update_thread.is_alive()
    
    save_config(config)
    logging.info("Configuration updated in real-time.")

def on_start():
    global config, stop_event, selected_sources, update_thread

    # Update config FIRST before validation
    update_config_file()
    time.sleep(0.5)  # Add small delay for file I/O
    config = load_config()
    
    # Validate WE path using FRESH config
    if var_wallpaper_engine.get() and not validate_we_path():
        return

    selected_sources = [
        source for source, var in [("unsplash", var_unsplash),
                                  ("pexels", var_pexels),
                                  ("wallpaper_engine", var_wallpaper_engine)]
        if var.get()
    ]

    if not selected_sources:
        messagebox.showinfo("No Selection", "No sources selected. Stopping wallpaper updates.")
        logging.info("No sources selected. Stopping wallpaper updates.")
        return

    # Update and reload config.
    update_config_file()
    config = load_config()
    logging.info("Configuration reloaded.")

    # print("Starting wallpaper update thread...")
    update_thread = threading.Thread(
        target=start_wallpaper_update,
        daemon=True
    )
    stop_event.clear()
    update_thread.start()
    root.update()  # Force immediate GUI refresh

    config['UPDATE_RUNNING'] = True
    save_config(config)
    print("Wallpaper update thread started. UPDATE_RUNNING set to True.")
    
    # (The remaining UI updates continue below …)
    var_unsplash.set(config.get('SOURCE_UNSPLASH', False))
    var_pexels.set(config.get('SOURCE_PEXELS', False))
    var_wallpaper_engine.set(config.get('SOURCE_WALLPAPER_ENGINE', True))
    chk_unsplash.grid(row=0, column=0, sticky="w", pady=2)

    if config.get('SOURCE_WALLPAPER_ENGINE', False):
        we_path = Path(config.get('SAVE_LOCATION', ''))
        if not we_path.exists():
            messagebox.showerror("Configuration Error",
                                 "Invalid save location. Set it in Config -> Advanced Settings")
            print("Save location validation failed (post thread start).")

def on_stop():
    global update_thread
    if update_thread and update_thread.is_alive():
        logging.info("Stopping the update thread.")
        stop_event.set()  # Signal the thread to stop
        update_thread.join()  # Wait for the thread to finish
        update_thread = None  # Reset the update_thread variable

    # Update the configuration settings in the config.json file
    config = load_config()
    config['UPDATE_RUNNING'] = False
    save_config(config)

    logging.info("Wallpaper update process stopped.")

def on_startup_checkbox_change():
    set_startup(var_startup.get())

def on_close():
    """Handle the window close event."""
    update_config_file()
    global update_thread
    logging.info("Initiating shutdown sequence")
    
    # Signal all components to stop first
    stop_event.set()
    
    # Terminate depotdownloader processes
    terminate_depotdownloader()
    
    # Wait for thread with timeout
    if update_thread and update_thread.is_alive():
        update_thread.join(2)  # Reduced from 5 seconds
        
    # Force kill any remaining processes
    terminate_depotdownloader()
    
    # Final cleanup before destruction
    root.destroy()

def save_credentials():
    """Save credentials to .env file"""
    env_path = os.path.join(os.path.dirname(__file__), '.env') if not getattr(sys, 'frozen', False) \
        else os.path.join(os.path.dirname(sys.executable), '.env')
    
    credentials = [
        f"UNSPLASH_ACCESS_KEY={unsplash_entry.get().strip()}",
        f"PEXELS_API_KEY={pexels_entry.get().strip()}",
        f"USERNAMES={username_entry.get().strip()}",
        f"PASSWORDS={password_entry.get().strip()}"
    ]
    
    try:
        with open(env_path, 'w') as f:
            f.write("\n".join(credentials))
        messagebox.showinfo("Success", "Credentials saved successfully!\nRestart the application to apply changes.")
    except Exception as e:
        messagebox.showerror("Error", f"Failed to save credentials: {str(e)}")
    finally:
        load_env_vars()  # Reload environment variables

def validate_we_path():
    """Validate Wallpaper Engine installation path."""
    global config
    path = config.get('SAVE_LOCATION', '')
    if not path:
        status_canvas.itemconfig(status_light, fill="red")
        return False
    
    we_path = Path(path)
    required_files = ["wallpaper32.exe", "wallpaper64.exe"]
    valid = we_path.exists() and all((we_path / file).exists() for file in required_files)
    
    status_canvas.itemconfig(status_light, fill="green" if valid else "red")
    return valid

def on_we_check():
    # Reload config first to get latest path
    config = load_config()
    entry_save_location.delete(0, tk.END)
    entry_save_location.insert(0, config.get('SAVE_LOCATION', ''))
    if var_wallpaper_engine.get() and not validate_we_path():
        var_wallpaper_engine.set(False)

# Create GUI variables linked to config values
var_unsplash = tk.BooleanVar(value=config['SOURCE_UNSPLASH'])
var_pexels = tk.BooleanVar(value=config['SOURCE_PEXELS'])
var_wallpaper_engine = tk.BooleanVar(value=config['SOURCE_WALLPAPER_ENGINE'])
var_save_old_wallpapers = tk.BooleanVar(value=config['SAVE_OLD_WALLPAPERS'])
var_startup = tk.BooleanVar(value=is_startup_enabled())
save_location_var = tk.StringVar(value=config['SAVE_LOCATION'])
check_interval_var = tk.StringVar(value=config['CHECK_INTERVAL'])
collections_url_var = tk.StringVar(value=config['COLLECTIONS_URL'])
wallpaper_limit_var = tk.StringVar(value=config['WALLPAPER_DOWNLOAD_LIMIT'])
max_wallpapers_var = tk.StringVar(value=config['MAX_WALLPAPERS'])

# Configure grid weights for proper resizing
root.grid_rowconfigure(1, weight=1)
root.grid_columnconfigure(0, weight=1)

# Create notebook
notebook = ttk.Notebook(root)
notebook.grid(row=0, column=0, padx=10, pady=10, sticky="nsew")

# Main Settings Tab
main_frame = ttk.Frame(notebook)
notebook.add(main_frame, text="Main Settings")

# Sources Section
sources_frame = ttk.LabelFrame(main_frame, text="Wallpaper Sources", padding=10)
sources_frame.grid(row=0, column=0, padx=5, pady=5, sticky="nw")

chk_unsplash = ttk.Checkbutton(sources_frame, text="Unsplash", variable=var_unsplash)
chk_pexels = ttk.Checkbutton(sources_frame, text="Pexels", variable=var_pexels)
chk_wallpaper_engine = ttk.Checkbutton(sources_frame, text="Wallpaper Engine", variable=var_wallpaper_engine)

chk_unsplash.grid(row=0, column=0, sticky="w", pady=2)
chk_pexels.grid(row=1, column=0, sticky="w", pady=2)
chk_wallpaper_engine.grid(row=2, column=0, sticky="w", pady=2)

# Configuration Settings
config_frame = ttk.LabelFrame(main_frame, text="Configuration", padding=10)
config_frame.grid(row=0, column=1, padx=5, pady=5, sticky="nsew")

# Add status indicator next to save location
status_canvas = tk.Canvas(config_frame, width=20, height=20, bd=0, highlightthickness=0)
status_canvas.grid(row=0, column=2, padx=5)
status_light = status_canvas.create_oval(2, 2, 18, 18, fill="red")

var_save_old_wallpapers = tk.BooleanVar()
chk_save_old_wallpapers = ttk.Checkbutton(config_frame, text="Save Old Wallpapers", variable=var_save_old_wallpapers)
chk_save_old_wallpapers.grid(row=6, column=0, sticky="w", pady=2)

chk_startup = ttk.Checkbutton(config_frame, text="Start with Windows", 
                            variable=var_startup, command=on_startup_checkbox_change)
chk_startup.grid(row=7, column=0, sticky="w", pady=2)

# API Credentials Tab
creds_frame = ttk.Frame(notebook)
notebook.add(creds_frame, text="Credentials")

ttk.Label(creds_frame, text="Unsplash Access Key:").grid(row=0, column=0, padx=5, pady=5, sticky="w")
unsplash_entry = ttk.Entry(creds_frame, width=40, show="*")
unsplash_entry.grid(row=0, column=1, padx=5, pady=5)
unsplash_entry.insert(0, os.getenv("UNSPLASH_ACCESS_KEY", ""))

ttk.Label(creds_frame, text="Pexels API Key:").grid(row=1, column=0, padx=5, pady=5, sticky="w")
pexels_entry = ttk.Entry(creds_frame, width=40, show="*")
pexels_entry.grid(row=1, column=1, padx=5, pady=5)
pexels_entry.insert(0, os.getenv("PEXELS_API_KEY", ""))

ttk.Label(creds_frame, text="Steam Usernames (comma-separated):").grid(row=2, column=0, padx=5, pady=5, sticky="w")
username_entry = ttk.Entry(creds_frame, width=40)
username_entry.grid(row=2, column=1, padx=5, pady=5)
username_entry.insert(0, os.getenv("USERNAMES", ""))

ttk.Label(creds_frame, text="Steam Passwords (comma-separated):").grid(row=3, column=0, padx=5, pady=5, sticky="w")
password_entry = ttk.Entry(creds_frame, width=40, show="*")
password_entry.grid(row=3, column=1, padx=5, pady=5)
password_entry.insert(0, os.getenv("PASSWORDS", ""))

save_creds_btn = ttk.Button(creds_frame, text="Save Credentials", command=save_credentials)
save_creds_btn.grid(row=4, column=1, pady=10)

# Logs Section
logs_frame = ttk.LabelFrame(root, text="Logs", padding=10)
logs_frame.grid(row=1, column=0, padx=10, pady=10, sticky="nsew")

log_text = scrolledtext.ScrolledText(logs_frame, state='disabled', width=70, height=10)
log_text.pack(expand=True, fill="both")

# Control Buttons
control_frame = ttk.Frame(root, padding=10)
control_frame.grid(row=2, column=0, padx=10, pady=10, sticky="ew")

btn_start = ttk.Button(control_frame, text="Start", command=on_start)
btn_stop = ttk.Button(control_frame, text="Stop", command=on_stop)
btn_start.pack(side="left", padx=5)
btn_stop.pack(side="left", padx=5)

# Configure grid weights for main frame
main_frame.grid_rowconfigure(0, weight=1)
main_frame.grid_columnconfigure(1, weight=1)

ttk.Label(config_frame, text="Save Location:").grid(row=0, column=0, sticky="e")
entry_save_location = ttk.Entry(config_frame, width=40, textvariable=save_location_var)
entry_save_location.grid(row=0, column=1, padx=5, pady=5)
save_location_var.trace_add('write', lambda *_: (update_config_file(), validate_we_path()))
validate_we_path()  # Add this line to validate on startup


ttk.Label(config_frame, text="Check Interval (seconds):").grid(row=1, column=0, sticky="e")
entry_check_interval = ttk.Entry(config_frame, width=20, textvariable=check_interval_var)
entry_check_interval.grid(row=1, column=1, padx=5, pady=5)
check_interval_var.trace_add('write', lambda *_: update_config_file())


ttk.Label(config_frame, text="Collections URL:").grid(row=3, column=0, sticky="e")
entry_collections_url = ttk.Entry(config_frame, width=40, textvariable=collections_url_var)
entry_collections_url.grid(row=3, column=1, padx=5, pady=5)
collections_url_var.trace_add('write', lambda *_: update_config_file())


ttk.Label(config_frame, text="Wallpaper Download Limit:").grid(row=4, column=0, sticky="e")
entry_wallpaper_download_limit = ttk.Entry(config_frame, width=20, textvariable=wallpaper_limit_var)
entry_wallpaper_download_limit.grid(row=4, column=1, padx=5, pady=5)
wallpaper_limit_var.trace_add('write', lambda *_: update_config_file())


ttk.Label(config_frame, text="Max Wallpapers:").grid(row=5, column=0, sticky="e")
entry_max_wallpapers = ttk.Entry(config_frame, width=20, textvariable=max_wallpapers_var)
entry_max_wallpapers.grid(row=5, column=1, padx=5, pady=5)
max_wallpapers_var.trace_add('write', lambda *_: update_config_file())

chk_unsplash = ttk.Checkbutton(sources_frame, text="Unsplash", variable=var_unsplash)
chk_pexels = ttk.Checkbutton(sources_frame, text="Pexels", variable=var_pexels)
chk_wallpaper_engine = ttk.Checkbutton(sources_frame, text="Wallpaper Engine", variable=var_wallpaper_engine)
chk_unsplash.grid(row=0, column=0, sticky="w", pady=2)
chk_pexels.grid(row=1, column=0, sticky="w", pady=2)
chk_wallpaper_engine.grid(row=2, column=0, sticky="w", pady=2)

# Bind the update_config_file function to the checkbox state changes
var_unsplash.trace_add('write', lambda *args: update_config_file())
var_pexels.trace_add('write', lambda *args: update_config_file())
var_wallpaper_engine.trace_add('write', lambda *args: update_config_file())
var_save_old_wallpapers.trace_add('write', lambda *args: update_config_file())

# Bind the on_close function to the window close event
root.protocol("WM_DELETE_WINDOW", on_close)

# Configure root logger early in initialization
logger = logging.getLogger()
logger.handlers.clear()  # Remove all existing handlers

# Add GUI handler
gui_handler = TextHandler(log_text)
gui_handler.setFormatter(logging.Formatter('%(asctime)s - %(levelname)s - %(message)s'))
logger.addHandler(gui_handler)

# Add stream handler for console
console_handler = logging.StreamHandler()
console_handler.setFormatter(logging.Formatter('%(asctime)s - %(levelname)s - %(message)s'))
logger.addHandler(console_handler)

logger.setLevel(logging.INFO)

# After all GUI elements are initialized (around line 456)
if config.get('UPDATE_RUNNING', False):
    root.after(100, on_start)  # Small delay to ensure GUI is fully initialized

# Run the Tkinter event loop
root.mainloop()