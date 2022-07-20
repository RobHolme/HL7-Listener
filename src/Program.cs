// Rob Holme (rob@holme.com.au)
// 02/06/2015

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HL7ListenerApplication
{
	class Program
	{
		private static int port = 5000;
		private static string filePath = null;
		private static bool sendACK = true;
		private static string passthruHost;
		private static int passthruPort;
		private static Encoding encoding = Encoding.Default;

		static void Main(string[] args)
		{
			// parse command line arguments
			if (ParseArgs(args))
			{
				// create a new instance of HL7TCPListener. Set optional properties to rturn ACKs, passthru messages, archive locaiton. Start the listener.
				HL7TCPListener listener = new HL7TCPListener(port, encoding);
				listener.SendACK = sendACK;
				if (filePath != null)
				{
					listener.FilePath = filePath;
				}
				if (passthruHost != null)
				{
					listener.PassthruHost = passthruHost;
					listener.PassthruPort = passthruPort;
				}
				if (!listener.Start())
				{
					LogWarning("Exiting");
				}
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
						if (i + 1 < cmdArgs.Length)
						{
							filePath = cmdArgs[i + 1];
							//  validate the the directory exists
							if (!System.IO.Directory.Exists(filePath))
							{
								LogWarning("The directory " + filePath + " does not exist");
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
						sendACK = false;
						break;
					// determine if the messages should be passed through to a remote host. Identify the hostname/IP and port.
					case "-PASSTHRU":
					case "--PASSTHRU":
					case "-T":
						if (i + 1 < cmdArgs.Length)
						{
							string[] temp = cmdArgs[i + 1].Split(':');
							if (temp.Length == 2)
							{
								passthruHost = temp[0];
								try
								{
									passthruPort = int.Parse(temp[1]);
								}
								catch (FormatException)
								{
									LogWarning("The port number provided needs to be an integer between 1 and 65535");
									return false;
								}
								if ((passthruPort == 0) || (passthruPort > 65535))
								{
									LogWarning("The port number provided needs to be an integer between 1 and 65535");
									return false;
								}
							}
							else
							{
								LogWarning("The passthru host and IP where not provided. eg -passthru host:port");
								return false;
							}
						}
						break;
					// set the text encoding method for the received message
					case "-ENCODING":
					case "--ENCODING":
					case "-E":
						if (i + 1 < cmdArgs.Length)
						{
							switch (cmdArgs[i + 1].ToUpper())
							{
								case "ASCII":
									encoding = Encoding.ASCII;
									break;
								case "UTF8":
								case "UTF-8":
									encoding = Encoding.UTF8;
									break;
								case "LATIN1":
								case "ISO-8859-1":
									encoding = Encoding.GetEncoding("ISO-8859-1");
									break;
								default:
									encoding = Encoding.Default;
									break;
							}
						}
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
			Console.WriteLine(" HL7Listener - v1.3 - Robert Holme. A simple MLLP listener to archive HL7 messages to disk.");
			Console.WriteLine(" Usage:");
			Console.WriteLine("");
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine(" HL7Listener.exe -Port <PortNumber> [-FilePath <path>] [-NoACK] [-Passthru <host>:<port>]");
			Console.ResetColor();
			Console.WriteLine("");
			Console.WriteLine("    -Port <PortNumber> specifies the port to listen on. Must be an integer between 1025 and 65535");
			Console.WriteLine("");
			Console.WriteLine("    -FilePath <Path> The path to archive the received messages to. If no path is supplied, messages will be saved");
			Console.WriteLine("                     to the directory the application is launched from.");
			Console.WriteLine("");
			Console.WriteLine("    -NoACK prevents ACKs from being sent. Without this switch ACKs will always be sent, even if not requested in MSH-15.");
			Console.WriteLine("");
			Console.WriteLine("    -Passthru <host>:<port> Pass all messages received through to the remote host. eg -Passthru somehost:5000");
			Console.WriteLine("");
			Console.WriteLine("    -Encoding <UTF8|Latin1|ASCII> Optionally define the text encoding for received messages. Defaults to system default encoding (UTF8).");
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
