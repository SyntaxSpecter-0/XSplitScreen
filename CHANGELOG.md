## 4.1.2

- Fixed an error when backing out of the splitscreen menu or selecting a screen after resetting the screens, which could leave controller input unresponsive.
- Fixed an error when leaving the splitscreen menu before the empty-screens prompt or countdown had been shown.
- Fixed an issue where the controller and keyboard/mouse would both control the first player when starting a game with Online enabled.
- Added a `Log Channels > Input` option that logs every button press to help with bug reports.

## 4.1.1

- Added the PS5 icon for DualSense controllers.
- Stopped rendering the main menu scene behind the splitscreen menu.
- Fixed errors when the cursor hovers over the Game Mode arrows.
- Sprites now use a shared asset (`AddComponent` replaced with `EnsureComponent` in the Unity project).

#### Ask before starting with unassigned screens

- When everyone is ready but some screen regions have no player, a Confirm/Decline prompt now lets players start anyway.
- Made the Confirm/Decline prompt appear over the region grid.

#### Fix splitscreen picker, Seeker and lobby panel bugs

- LookingGlass soft dependency: its "input closes menu" option reacted to every player's input. It now correctly uses input from the player who opened it.
- Moving a player to another display no longer stretches their slot. Each display's canvas has its own scale factor.
- Seeker meditate: A spectating player's HUD gets its own copy of the input overlay. This fixes an issue where the player who was Seeker saw only white icons, even when performing inputs correctly. All copies are now coloured.
- Artifacts, Expansions, Karma popouts and the crafting panel are now fitted to the player's viewport using the new `FitToViewport` component. This replaces the old one-shot scaling.

## 4.1.0

- Redesigned the player configuration panel (profile, colour and trails tabs) by SyntaxSpecter.
- Controller button prompts now show the correct glyph for the connected gamepad (Xbox/PlayStation).
- Updated README screenshots and GIFs to match the new UI.

## 4.0.9

- Fixed the UI becoming deactivated when the DLC is missing.

## 4.0.8

- Updated credits.

## 4.0.7

- Added local multiplayer pause support.
- Changed how extra main menu buttons are removed to avoid conflicts with buttons added by R2API.

## 4.0.6

- Added a Game Mode selector to the splitscreen menu, so the chosen mode is used when launching.
- Reworked how multiplayer player colours are applied, fixing a conflict with other mods that also patch player colours.

## 4.0.5

- Added game mode selection to the XSplitScreen menu.

## 4.0.3

- Fixed a startup crash caused by a RoR2 UI API change (`Nameplate.SetBody` was replaced with `Initialize`).
- Fixed a disappearing cursor bug.

## 4.0.1

- Made swapping between multiple displays easier.
- Updated language tokens.

## 4.0.0

- Complete rewrite.
- New UI.
- Support for SotS.
- More varied screen assignments.
- Integrated credits.

## 3.1.4

- Disabled dev mode.

## 3.1.3

- Fixed a crash caused by the new update. Thanks, Narl! (Multiplayer support not guaranteed.)
- Added translations by LuizFilipeRN.
- Removed the Ko-fi button.
- Updated the Discord button with Lemonization.

## 3.1.2

- Removed Patreon and PayPal buttons.
- Added a Ko-fi button.
- Added support for custom language files.

## 3.1.1

- Disabled dev mode.

## 3.1.0

This patch is dedicated to BluJay in honour of their generous donation. Thank you!

- Added a slider for HUD scale.
- Added a PayPal button.
- Fixed an issue where dragging sliders did not work with controllers.
- Fixed a miscellaneous console error.
- Fixed a Lemonization issue.

## 3.0.1

This patch is dedicated to NickmitdemKopf in honour of their generous donation. Thank you!

- Added an error message for an insufficient number of profiles (this will delete existing language files).
- Added the ability to scale the HUD per user by manually editing the `_hudScale` value in the `assignments.json` file. A GUI option is in development.
- Fixed an issue where UI scaling made the assignment window unusable at certain resolutions.
- Fixed incorrect bindings being displayed during runs.

## 3.0.0

This update is a full rebuild of the entire mod. Many bugs have been fixed, while a few have been added. A lot of time went into this project, resulting in what will hopefully be considered the closest Risk of Rain 2 can get to native splitscreen. Instead of listing every change, only the most notable ones are listed below:

- Gamepad cursors work on most UI elements and on all monitors.
- Preferences are properly associated with profiles.
- Added localisation support via language files.

## 2.0.8

- Fixed character selection bugs.

## 2.0.7

- Cleaned up logging.
- Aligned the reset button.
- Updated README.

## 2.0.6

- API change without adhering to versioning rules (expect more of this).
- Removed the reset label.

## 2.0.5

- Added an assignment reset button.

## 2.0.4

- Added a button for Patreon.

## 2.0.3

- Fixed a bug where controllers were unable to interact with the UI.

## 2.0.2

- Added a fancy colour cycle for the warning triangle.
- Refactored the namespace.

## 2.0.1

- Added assignment notifications for invalid options.
- Added log levels.

## 2.0.0

- Added the assignment screen.
- Added a config entry for automatically enabling the mod on startup.
- Changed player names to reflect the local profile name in most cases.
- Changed player highlights to reflect the preferred colour.
- Changed the cursor centre to the centre of the assigned screen.
- Fixed most issues with gamepad cursors.
- Fixed character selection.
- Fixed artifact selection.
- Fixed most keybinds not loading.
- The mod now uses the BepInEx log and config file to store preferences.
- Changing bindings in the settings menu now saves the bind to the player who last moved their cursor (sometimes breaks during runs).

## 1.2.1

- Fixed the character selection Commando invasion bug.
- Added a temporary fix for non-existent controllers being recognised. If you have problems with some controllers not being recognised, you'll have to wait until the profile selection window has been completed. That should happen in the next month or so.

## 1.2.0

- Added vertical splitscreen. If mods that manipulate the camera cause issues, fall back to version 1.1.2.

## 1.1.2

- Fixed chat and console.
- Most clickable UI buttons should now work with the gamepad cursor.
- Fixed item selection via the Command Artifact.
- Removed screen blur when viewing the scoreboard.
- Added a Discord link and logging for better troubleshooting.

## 1.1.1

- Added a dependency string for R2API. This should fix the mod not loading if R2API was not previously installed.

## 1.1.0

- Added a menu.
- Added the ability to enable splitscreen for connected devices with one click.
- Temporarily removed the add and remove player buttons.
- Cursors should now work with all gamepads.
- Full gamepad support is being tested.
- Various bug fixes.

## 1.0.0

- First release.