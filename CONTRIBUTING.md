## Contribution

We welcome contributions for our teasing lines component of the program.

---

### Adding New Teasing Phrases (Callouts)

The application uses **JSON files** to manage all spoken teasing phrases (Callouts). You can easily contribute new
phrases without touching the Python code.

#### 1. Locate the Files

All callout data is stored in the `./res/callouts/` directory. Each file corresponds to a language code (e.g.,
`en.json`, `de.json`).

#### 2. Understand the Structure

The JSON structure is based on **Trigger Keys**. When a specific event happens in the app (e.g., the beat speeds up),
the corresponding key is used to randomly select one phrase from the array.

```json
{
  "beat_change_general": [
    "Phrase 1 for general beat change.",
    "Phrase 2 for general beat change."
  ],
  "beat_change_faster": [
    "This is a faster beat phrase."
  ]
  // ... and so on for all keys
}
```

The available Trigger Keys at the moment are:

| Trigger Key           | Description                                                                                  |
|:----------------------|:---------------------------------------------------------------------------------------------|
| `beat_change_general` | Fired when the beat changes frequency or rhythm.                                             |
| `beat_change_faster`  | Fired when the beat frequency increases significantly.                                       |
| `beat_change_slower`  | Fired when the beat frequency decreases significantly.                                       |
| `pause_start`         | Fired when the session enters an intentional pause.                                          |
| `pause_end`           | Fired when the session resumes after a pause.                                                |
| `media_skipped`       | Fired when the user skips the currently displayed media.                                     |
| `media_repeat`        | Fired when the user decides to repeat the current media file by pressing the `previous` key. |

#### 3. How to Contribute Phrases

To add a new phrase, simply append your new text string to the relevant array within the existing language file (e.g.,
`en.json`):

```json
"beat_change_faster": [
    "The speed is picking up. Can you keep the rhythm?",
    "Go faster! Your endurance is being tested.",
    "Your new exciting phrase goes here!" ⬅️ **ADD IT HERE**
],
```

--- 

### Adding New Languages

To add a completely new language (e.g., French), you need to create a new JSON file based on the corresponding language
code.

#### 1. Create the New File

Create a new file in the `./res/callouts/` directory and name it using the standard two-letter language code (e.g.,
`fr.json`).

#### 2. Duplicate the Structure

Copy the entire contents of an existing file (like `en.json`) into your new `fr.json` file. Ensure that all required
Trigger Keys are present, even if their arrays are temporarily empty. The CalloutHandler depends on the presence of
these keys.

#### 3. Translate the Content

Translate the phrases within the arrays of your new file. Or add completely new ones. That is up to you and nobody
else :D

Once the file is saved, the application's Settings Window will automatically detect the new language and make it
available for selection.

---

### 🚀 Submitting Your Contribution (Pull Requests)

Once you have added new phrases, created a new language file, or made code changes, please follow these steps to submit your work:

#### 1. Branching

All contributions must be made from a new feature branch, not directly to the `main` branch.

```bash
git checkout main
git pull
git checkout -b feature/add-french-callouts  # Use a descriptive name
```

#### 2. Commit Messages
Please ensure your commit messages are descriptive and reference the area you are modifying.

#### 3. Create the Pull Request (PR)
- Push your new branch to your fork.

- Open a Pull Request targeting the main branch of the original repository.

- PR Title: Use a clear, concise title that summarizes your work.

- PR Description: Describe what you added, why it improves the experience, and which files were changed (especially for new language files).

- We will review your PR as quickly as possible. Thank you for making the app better!