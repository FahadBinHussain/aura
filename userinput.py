import tkinter as tk
from tkinter import scrolledtext
import os

def submit():
    global user_input
    user_input = text_area.get("1.0", tk.END).strip()
    root.destroy()

# Set up the UI window
root = tk.Tk()
root.title("AI Agent Input")
root.attributes('-topmost', True) # Keeps the window on top of your editor

tk.Label(root, text="Write your prompt for the AI. Click Submit when done.", font=("Arial", 10)).pack(pady=5)

text_area = scrolledtext.ScrolledText(root, wrap=tk.WORD, width=60, height=15, font=("Arial", 11))
text_area.pack(padx=10, pady=5)

tk.Button(root, text="Submit to AI", command=submit, bg="green", fg="white", font=("Arial", 12, "bold")).pack(pady=10)

user_input = ""
root.mainloop() # This completely blocks the script until the window is closed

# Write output to a file instead of stdout, because the detached window can't print directly back to the agent
with open("agent_response.txt", "w", encoding="utf-8") as f:
    f.write(user_input if user_input else "stop")