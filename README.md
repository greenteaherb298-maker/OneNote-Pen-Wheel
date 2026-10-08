# OneNote Pen Wheel 🎨
A simple radial pen selection wheel for Microsoft OneNote, made with C# and Claude. Supports up to 8 pens, Surface Pen shortcuts, and draggable positioning. Contributions welcome!

Hey guys! I made a little radial pen wheel for Microsoft OneNote using C# and Claude because I got tired of manually switching pen colors.

I use a Microsoft M1776 Surface Pen with an HP OmniBook 7 Flip 16", and I wanted a quicker way to switch between my pens while taking notes.

I'm not really a programmer, so this is a pretty simple and janky project that could definitely use some improvements!

## Features

- Switch between up to 8 OneNote pens/highlighters.
- Open the wheel using `Ctrl + Alt + F12`.
- Works with remapped stylus buttons using AutoHotkey.
- Drag the wheel around your screen.
- Remembers its last position.
- Runs in the Windows system tray.

## How to run

You'll need the **.NET 8 SDK** installed to compile the program.

1. Download the repository as a ZIP and extract it.
2. Open the extracted folder containing `Program.cs` and `OneNotePenWheel.csproj`.
3. Click the File Explorer address bar, type `cmd`, and press Enter.
4. Paste this command:

   `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`

5. Once it's finished compiling, the EXE will be in:

   `bin\Release\net8.0-windows\win-x64\publish\`

6. Open desktop OneNote with the Draw tab visible, then run the EXE.
7. Press `Ctrl + Alt + F12` to open the wheel!

I personally use AutoHotkey to map the top eraser button on my Surface Pen to this shortcut.

## Known limitations

- The wheel doesn't follow the stylus position. You have to drag it manually, but it remembers its position.
- There's a slight delay when opening the wheel and switching pens, which may be because of my surface pro pen or AHK script.
- Only supports up to 8 pens/highlighters.
- OneNote must be open with the Draw tab visible.

## Contributions welcome!

I mostly vibe-coded this with Claude, so there's probably a lot that could be improved.

If you know C#, AutoHotkey, or Windows stylus programming, please feel free to fork the project, submit pull requests, or suggest improvements!

I'd especially love help with getting the wheel to open next to the pen tip, reducing the delay, and making it more seamless.

Hopefully this helps someone or gives someone an idea for an even better solution! :)
