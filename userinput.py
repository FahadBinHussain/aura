import os
import time
import sys

input_file = "user_prompt.txt"

template = """Type your instructions below. Auto-save is safe!
When you are completely finished, type  /go  at the very end and save.

--- TYPE BELOW THIS LINE ---
"""

with open(input_file, "w", encoding="utf-8") as f:
    f.write(template)

print(f"\n[!] Agent paused. Please open '{input_file}' in your editor.")
print("[!] Type your prompt, add '/go' at the end, and save.\n")

# Infinite loop checking the file every 1 second
while True:
    time.sleep(1)
    try:
        with open(input_file, "r", encoding="utf-8") as f:
            content = f.read().strip()
        
        # Trigger ONLY if the very last thing in the file is /go
        if content.endswith("/go"):
            # Extract only the user's text below the line
            parts = content.split("--- TYPE BELOW THIS LINE ---")
            if len(parts) > 1:
                raw_input = parts[1].strip()
                # Remove the '/go' from the final text the AI sees
                user_input = raw_input[:-3].strip() 
            else:
                user_input = "stop"
            
            # Clean up the file
            try:
                os.remove(input_file)
            except:
                pass
            
            # Print the result so the agent reads it, then exit the script
            print(f"User provided: {user_input if user_input else 'stop'}")
            sys.exit(0)
            
    except Exception:
        pass # Ignore file read errors if the editor is currently saving