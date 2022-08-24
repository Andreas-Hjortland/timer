# Timer
A simple console app to track hours worked

This app will query the windows event log for login / logout events (and related
events for RDP sessions and screen lock/unlock) and present time worked in a
simple tabular interface.

To build just run `dotnet publish --configuration Release --output ./` and
launch the `Timer.exe` file that appears in your working directory.

Run it with the `-h` flag to see possible options and flags 
