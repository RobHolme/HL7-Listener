// Rob Holme (rob@holme.com.au)
// 30/05/2015

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HL7ListenerApplication
{
    class Program
    {
        private static int port = 0;
        private static string filePath = null;
        private static bool sendACK = true;

        static void Main(string[] args)
        {
            // parse command line arguments
            if (ParseArgs(args))
            {
                // create a new instance of HL7TCPListener
                HL7TCPListener listener = new HL7TCPListener(port, filePath, sendACK);
            }
        }


        /// <summary>
        /// Parse the command line arguements
        /// </summary>
        /// <param name="args"></param>
        static bool ParseArgs(string[] cmdArgs)
        {
            // parse command line switches
            if (cmdArgs.Count() < 2)
            {
                Usage();
                return false;
            }
            for (int i = 0; i < cmdArgs.Length; i++)
            {
                switch (cmdArgs[i].ToUpper())
                {
                    // parse the port parameter
                    case "-PORT":
                    case "-P":
                    case "--PORT":
                    case "--P":
                        if (i + 1 < cmdArgs.Length) 
                        {
                            try
                            {
                                port = int.Parse(cmdArgs[i + 1]);
                            }
                            // catch exceptions if the user enters a non integer values
                            catch (FormatException)
                            {
                                LogWarning("The port number provided needs to be an integer between 1025 and 65535");
                                return false;
                            }
                            // validate the port entered is witin the correct range. 
                            if ((port < 1025) || (port > 65535))
                            {
                                LogWarning("The port number must be an integer between 1025 and 65535");
                                return false;
                            }
                        }
                        else
                        {
                            Usage();
                            return false;
                        }
                        break;
                    //  parse the filepath parameter
                    case "-FILEPATH":
                    case "--FILEPATH":
                    case "-F":
                    case "--F":
                        if (i + 1 < cmdArgs.Length)
                        {
                            filePath = cmdArgs[i + 1];
                            //  validate the the directory exists
                            if (!System.IO.Directory.Exists(filePath))
                            {
                                LogWarning("The directory " + filePath + " does not exst");
                                return false;
                            }
                            // append trailing slash if not present
                            if (!(filePath.Substring(filePath.Length - 1, 1) == "\\"))
                            {
                                filePath = filePath + "\\";
                            }
                        }
                        break;
                    // determine if ACKs should be suppressed.
                    case "-NOACK":
                    case "--NOACK":
                    case "-N":
                    case "--N":
                        sendACK = false;
                        break;
                }
            }
            return true;
        }


        /// <summary>
        /// Display help on command line parameters
        /// </summary>
        static void Usage()
        {
            Console.WriteLine("");
            Console.WriteLine(" HL7Listener - v1.0 - Robert Holme. A simple MLLP listener to archive HL7 messages to disk.");
            Console.WriteLine(" Usage:");
            Console.WriteLine("");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(" HL7Listener.exe -Port <PortNumber> [-FilePath <path>] [-NoACK]");
            Console.ResetColor();
            Console.WriteLine("");
            Console.WriteLine("    -Port <PortNumber> specifies the port to listen on. Must be an integer between 1025 and 65535");
            Console.WriteLine("");
            Console.WriteLine("    -FilePath <Path> The path to archive the received messages to. If no path is supplied, messsages will be saved"); 
            Console.WriteLine("                     to the directory the application is launched from.");
            Console.WriteLine("");
            Console.WriteLine("    -NoACK prevents ACKs from being sent even if the received messages requests an Accept Acknowledgement");
            Console.WriteLine("           If the original message does not request an Accept Acknowledgement then no ACK will be sent regardless of this switch.");
            Console.WriteLine("");
        }

        /// <summary>
        /// Write a warning message to the console
        /// </summary>
        /// <param name="message"></param>
        static void LogWarning(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("WARNING: " + message);
            Console.ResetColor();
        }
    }
}
