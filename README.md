# HL7Listener

## Project Description
This is a command line application that listens for MLLP framed HL7 messages.  Any HL7 messages received are written to disk. This is intended as a simple diagnostic app to capture a stream of HL7 messages to disk for offline analysis/troubleshooting.

Acknowledgement (ACK) messages will be returned if the message received has the Accept-Acknowledgement field (MSH-15) set to `AL` . If this is not set, no ACK will be returned.  Additionally, ACKs can be prevented from being returned in all cases if the `-NoACK` command line switch is specified. 

HL7Listener supports simultaneous connections from multiple sources. It only supports MLLP framed HL7 messages.

## Running HL7Listener

```
HL7Listener.exe -Port <port-number> [-FilePath <path>] [-PassThru <host>:<port>] [-NoACK]
```

### Parameters

__-Port \<port-number\>__: specifies the TCP port to listen for incoming connections.  <port-number> must be in the range from 1025 to 65535.  
e.g. `HL7Listener -Port 5000`

__-FilePath \<path\>__:  Specifies the location to save the HL7 messages to. If no path is provided, the messagges will be saved to the current path of the console session that ran the application. 
e.g.  `HL7Listener -Port 5000 -FilePath c:\HL7\saved-messsages`

__-PassThru \<host\>:\<port\>__: Pass any messages received onto the remote host after saving the messages to disk. 
e.g. `HL7Listener -Port 5000 -FilePath c:\HL7\saved-messsages -passthru 192.168.0.50:6000`

__-NoACK__: suppresses acknowledgement messages from being returned regardless of the Accept-Acknowledgement value in the message received.  
e.g.  `HL7Listener -Port 5000 -FilePath c:\test -NoACK`


The file naming convention of the saved  files includes the date time stamp,  and random 6 digit sequence number, and the message trigger. e.g. `201505301529_028615_ADT^A01.hl7`. If multiple messages are received from the same TCP session, the sequence number will increment for each message. If the TCP connection is closed  and reopened for  each message sent, each file name will have a non sequential (random) sequence number.
