// Rob Holme (rob@holme.com.au)
// 30/05/2015

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;


namespace HL7ListenerApplication
{
    class HL7TCPListener
    {

        private TcpListener tcpListener;
        private Thread tcpListenerThread;
        private int listernerPort;
        private string archivePath;
        private bool sendACK;

        /// <summary>
        /// Constructor
        /// </summary>
        public HL7TCPListener(int port, string filePath =  null, bool ACK = true)
        {
            this.listernerPort = port;
            this.archivePath = filePath;
            this.sendACK = ACK;
            this.tcpListener = new TcpListener(IPAddress.Any, this.listernerPort);
            this.tcpListenerThread = new Thread(new ThreadStart(StartListener));
            this.LogInformation("Starting HL7 listener on port " + this.listernerPort);
            this.tcpListenerThread.Start(); 
        }


        /// <summary>
        /// Start listening for new connections
        /// </summary>
        private void StartListener()
        {
            try
            {
                this.tcpListener.Start();
                while (true)
                {
                    // waits for a client connection to the listener
                    TcpClient client = this.tcpListener.AcceptTcpClient();
                    this.LogInformation("New client connection accepted from " + client.Client.RemoteEndPoint);
                    // create a new thread. This will handle communication with a client once connected
                    Thread clientThread = new Thread(new ParameterizedThreadStart(ReceiveData));
                    clientThread.Start(client);
                }
            }
            catch (Exception e)
            {
                LogWarning("An error occurred while attempting to start the listener on port " + this.listernerPort);
                LogWarning(e.Message);
                LogWarning("HL7Listener exiting.");
            }
        }

        /// <summary>
        /// Receive data from a client connection, look for MLLP HL7 message.
        /// </summary>
        /// <param name="client"></param>
        private void ReceiveData(object client)
        {
            Random random = new Random(Guid.NewGuid().GetHashCode()); 
            int filenameSequenceStart = random.Next(0, 1000000); // generate 6 digit sequence number
            TcpClient tcpClient = (TcpClient)client;
            NetworkStream clientStream = tcpClient.GetStream();
            byte[] messageBuffer = new byte[4096];
            int bytesRead;
            String messageData = "";
            int messageCount = 0;

            while (true)
            {
                bytesRead = 0;
                try
                {
                    // Wait until a client application submits a message
                    bytesRead = clientStream.Read(messageBuffer, 0, 4096);
                }
                catch (Exception  e)
                {
                    // A network error has occurred
                    LogWarning("An exception has occurred while receiving the message from " + tcpClient.Client.RemoteEndPoint);
                    LogWarning(e.Message);
                    break;
                }
                if (bytesRead == 0)
                {
                    // The client has disconected
                    LogInformation("The client " + tcpClient.Client.RemoteEndPoint + " has disconnected");
                    break;
                }
                // Message buffer received successfully
                messageData += Encoding.UTF8.GetString(messageBuffer, 0, bytesRead);
                // Find a VT character, this is the beginning of the MLLP frame
                int start = messageData.IndexOf((char)0x0B);
                if (start >= 0)
                {
                    // Search for the end of the MLLP frame (a FS character)
                    int end = messageData.IndexOf((char)0x1C);
                    if (end > start)
                    {
                        messageCount++;
                        try
                        {
                            HL7Message message = new HL7Message(messageData.Substring(start + 1, end - (start + 1)));
                            messageData = ""; // reset the message data string for the next message
                            string messageTrigger = message.GetMessageTrigger(); // append the message trigger to the file name
                            string messageControlID = message.GetHL7Item("MSH-10")[0];
                            string acceptAckType = message.GetHL7Item("MSH-15")[0];
                            string dateStamp = DateTime.Now.Year.ToString() + DateTime.Now.Month.ToString() + DateTime.Now.Day.ToString() + DateTime.Now.Hour.ToString() + DateTime.Now.Minute.ToString();
                            string filename = dateStamp + "_" + (filenameSequenceStart + messageCount).ToString("D6") + "_" + messageTrigger + ".hl7"; //  increment sequence number for each filename
                            // Write the HL7 message to file.
                            WriteMessagetoFile(message.ToString(), this.archivePath + filename);
                            // send ACK message is MSH-15 is set to AL and ACKs not disbaled by -NOACK command line switch
                            if ((this.sendACK) && (acceptAckType.ToUpper() == "AL"))
                            {
                                LogInformation("Sending ACK (Message Control ID: " + messageControlID + ")");
                                // generate ACK Message and send in response to the message received
                                string response = GenerateACK(message.ToString());  // TO DO: send ACKS if set in message header, or specified on command line
                                byte[] encodedResponse = Encoding.UTF8.GetBytes(response);
                                // Send response
                                try
                                {
                                    clientStream.Write(encodedResponse, 0, encodedResponse.Length);
                                    clientStream.Flush();
                                }
                                catch (Exception e)
                                {
                                    // A network error has occurred
                                    LogWarning("An exception has occurred while sending an ACK to the client" + tcpClient.Client.RemoteEndPoint);
                                    LogWarning(e.Message);
                                    break;
                                }
                           
                            }
                        }
                        catch (Exception e)
                        {
                            messageData = ""; // reset the message data string for the next message
                            LogWarning("An exception occurred while parsing the HL7 message");
                            LogWarning(e.Message);
                            break;
                        }
                    }
                }
            }
            LogInformation("Total messages received:" + messageCount);
            clientStream.Dispose();
            tcpClient.Close();
        }


        /// <summary>
        /// /// <summary>
        /// Write the HL7 message recieved to file. Optionally provide the file path, otherwise use the working directory.     
        /// </summary>
        /// <param name="message"></param>
        /// <param name="filePath"></param>
        private void WriteMessagetoFile(string message, string filename)
        {
            // write the HL7 message to file
            try
            {
                LogInformation("Received message. Saving to file " + filename);
                System.IO.StreamWriter file = new System.IO.StreamWriter(filename);
                file.Write(message);
                file.Close();
            }
            catch (Exception e)
            {
                LogWarning("Failed to write file " + filename);
                LogWarning(e.Message);
            }
        }

        /// <summary>
        /// Generate a string containing the ACK message in response to the original message. Supply a string containing the original message (or at least the MSH segment).
        /// </summary>
        /// <returns></returns>
        string GenerateACK(string originalMessage)
        {
            // create a HL7Message object using the original message as the source to obtain details to reflect back in the ACK message
            HL7Message tmpMsg = new HL7Message(originalMessage);
            string trigger = tmpMsg.GetHL7Item("MSH-9.2")[0];
            string originatingApp = tmpMsg.GetHL7Item("MSH-3")[0];
            string originatingSite = tmpMsg.GetHL7Item("MSH-4")[0];
            string messageID = tmpMsg.GetHL7Item("MSH-10")[0];
            string processingID = tmpMsg.GetHL7Item("MSH-11")[0];
            string hl7Version = tmpMsg.GetHL7Item("MSH-12")[0];
            string ackTimestamp = DateTime.Now.Year.ToString() + DateTime.Now.Month.ToString() + DateTime.Now.Day.ToString() + DateTime.Now.Hour.ToString() + DateTime.Now.Minute.ToString();

            StringBuilder ACKString = new StringBuilder();
            ACKString.Append((char) 0x0B);
            ACKString.Append("MSH|^~\\&|HL7Listener|HL7Listener|" + originatingSite + "|" + originatingApp + "|" + ackTimestamp + "||ACK^" + trigger + "|" + messageID + "|" + processingID + "|" + hl7Version);
            ACKString.Append((char) 0x0D);
            ACKString.Append("MSA|CA|" + messageID);
            ACKString.Append((char) 0x1C);
            ACKString.Append((char) 0x0D);
            return ACKString.ToString();
        }


        /// <summary>
        /// Set and get the values of the SendACK option. This can be used to overide sending of ACK messages. 
        /// </summary>
        public bool SendACK
        {
            get { return this.sendACK; }
            set { this.sendACK = SendACK; }
        }

        /// <summary>
        /// Write informational event to the console.
        /// </summary>
        /// <param name="message"></param>
        private void LogInformation(string message)
        {
            Console.WriteLine(DateTime.Now + ": " +  message);
        }
        

        /// <summary>
        /// Write a warning message to the console
        /// </summary>
        /// <param name="message"></param>
        private void LogWarning(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("WARNING: " + message);
            Console.ResetColor();
        } 
    }
}
