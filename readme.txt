TCP Chat Application - NDS203 Assignment 2
==========================================

Developer: [Daniel Solano]
Student ID: [A00151824]
Date: [21-Mar-25]

Application Overview
====================
This is a fully functional TCP chat application with advanced features including:
- Username management
- Private messaging
- Moderator system
- Multiple command options
- Real-time message broadcasting

System Requirements
===================
- .NET Core 3.1 Runtime
- Windows OS
- Visual Studio 2019+ (for development)

How To Run the Program
======================
Basic Testing:
1. Run ONE instance as Server:
   - Launch the executable
   - Click "Host Server" button

2. Run ONE or MORE instances as Clients:
   - Launch additional executables
   - Click "Join Server" button
   - Set username when prompted

Advanced Testing:
For full feature testing, you'll need:
- 1 Server instance
- 2+ Client instances (to test private messaging and moderation)

Key Features
============
1. User Management:
   - Set username with !username [name]
   - Change username with !user [new_name]

2. Chat Commands:
   - !commands - View available commands
   - !who - List online users
   - !about - Server information
   - !time - Current server time

3. Private Messaging:
   - !whisper [username] - Start private chat
   - !global - Return to main chat

4. Moderator System (Server Only):
   - !mod [username] - Promote/demote users
   - !mods - List moderators
   - !kick [username] - Remove users

5. Custom Command:
   - !time - Displays current server time

Important Notes
===============
- The server must be running before clients can connect
- All clients must be on the same network
- Usernames must be unique and alphanumeric only
- Moderator commands only work from server console

Academic Integrity Declaration
=============================
I declare that except where referenced, this work is my own. I have read and understand Torrens University Australia's Academic Integrity Policy.

For any questions about this project, please contact:
[daniel.moreno@student.torrens.edu.au]