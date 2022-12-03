# HL7Listener

## Project Description
This is a command line application that listens for MLLP framed HL7 messages.  Any HL7 messages received are written to disk. This is intended as a simple diagnostic app to capture a stream of HL7 messages to disk for offline analysis/troubleshooting. Not intended for production use.

Acknowledgement (ACK) messages will be returned if the message received has the Accept-Acknowledgement field (MSH-15) set to `AL` . If this is not set, no ACK will be returned.  Additionally, ACKs can be prevented from being returned in all cases if the `-NoACK` command line switch is specified. 

HL7Listener supports simultaneous connections from multiple sources. It only supports MLLP framed HL7 messages.

## Build Instructions
The solution will target .Net 6.0.
1. Install the .Net 6.0 SDK. Install instructions for the SDK for each platform are available from:
* Linux: https://docs.microsoft.com/en-us/dotnet/core/linux-prerequisites?tabs=netcore2x
* Windows: https://docs.microsoft.com/en-us/dotnet/core/windows-prerequisites?tabs=netcore2x
* MacOS: https://docs.microsoft.com/en-us/dotnet/core/macos-prerequisites?tabs=netcore2x
1. Open a command console, navigate to the root folder of this solution (containing HL7Listener.csproj). Run the following build command:
`dotnet build --configuration Release`
2. The build process will copy a version for each .Net version to subfolders of `bin\release\`.

## Install Instructions (Instead of build)
If you do not wish to build from source, download the latest pre-built release from: https://github.com/RobHolme/HL7-Listener/releases
These are self-contained releases that include the relevant .Net dependencies, it does not require the .Net runtime to be installed.

## Running HL7Listener
Windows
```
HL7Listener.exe -Port <port-number> [-FilePath <path>] [-PassThru <host>:<port>] [-NoACK] [-Encoding <UTF8 | ASCII | Latin1>] [-TLS <certificate-path | certificate-thumbprint>]
```
Linux
```
HL7Listener -Port <port-number> [-FilePath <path>] [-PassThru <host>:<port>] [-NoACK] [-Encoding <UTF8 | ASCII | Latin1>] [-TLS <certificate-path>]
```
Press the 'ESC' key from the console to terminate the program. 

### Parameters

__-Port \<port-number\>__: specifies the TCP port to listen for incoming connections.  <port-number> must be in the range from 1025 to 65535.  

e.g. `HL7Listener -Port 5000`

__-FilePath \<path\>__:  Specifies the location to save the HL7 messages to. If no path is provided, the messages will be saved to the current path of the console session that ran the application. 

e.g.  `HL7Listener -Port 5000 -FilePath c:\HL7\saved-messages`

__-PassThru \<host\>:\<port\>__: Pass any messages received onto the remote host after saving the messages to disk. 

e.g. `HL7Listener -Port 5000 -FilePath c:\HL7\saved-messages -passthru 192.168.0.50:6000`

__-NoACK__: suppresses acknowledgement messages from being returned regardless of the Accept-Acknowledgement value in the message received.  

e.g.  `HL7Listener -Port 5000 -FilePath c:\test -NoACK`

__-Encoding \<UTF8 | ASCII | Latin1\>__: Specify an text encoding method for received messages. Optional - defaults to UTF8.

 e.g.  `HL7Listener -Port 5000 -FilePath c:\test -Encoding Latin1`

__-TLS \<certificate-path\>|\<certificate-thumbprint\>__: Require clients to connect using TLS. \<certificate-path\> should refer to a file containing a PFX (PKS12) certificate. User will be prompted for the certificate password (enter if no password). If a certificate thumbprint is provided instead, the Windows certificate store will be searched for a matching certificate instead. The certificate thumbprint option is only supported on Windows platforms, Linux platforms limited to providing a certificate file only.
Note: The TLS encryption will only apply to connections from remote clients, it will not apply to -PassThru connections.

The file naming convention of the saved files includes the date time stamp, and random 6 digit sequence number, and the message trigger. e.g. `201505301529_028615_ADT^A01.hl7`. If multiple messages are received from the same TCP session, the sequence number will increment for each message. If the TCP connection is closed and reopened for each message sent, each file name will have a non-sequential (random) sequence number.
