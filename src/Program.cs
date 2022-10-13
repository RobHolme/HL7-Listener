// Rob Holme (rob@holme.com.au)
// 02/06/2015

using System;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace HL7ListenerApplication {
	class Program {
		private static int port = 5000;
		private static string filePath = null;
		private static bool sendACK = true;
		private static string passthruHost;
		private static int passthruPort;
		private static Encoding encoding = Encoding.Default;
		private static bool useTLS = false;
		private static X509Certificate2 certificate = null;

		static void Main(string[] args) {
			// parse command line arguments
			if (ParseArgs(args)) {
				// create a new instance of HL7TCPListener. Set optional properties to return ACKs, passthru messages, archive location. Start the listener.
				HL7TCPListener listener;
				if (useTLS) {
					listener = new HL7TCPListener(port, encoding, certificate);
				}
				else {
					listener = new HL7TCPListener(port, encoding);
				}
				listener.ArchivePath = filePath;
				listener.SendACK = sendACK;
				if (passthruHost != null) {
					listener.PassthruHost = passthruHost;
					listener.PassthruPort = passthruPort;
				}
				if (!listener.Start()) {
					LogWarning("Exiting - failed to start socket listener.");
				}
			}
		}


		/// <summary>
		/// Parse the command line arguments
		/// </summary>
		/// <param name="args"></param>
		static bool ParseArgs(string[] cmdArgs) {
			// parse command line switches
			if (cmdArgs.Count() < 2) {
				Usage();
				return false;
			}
			for (int i = 0; i < cmdArgs.Length; i++) {
				switch (cmdArgs[i].ToUpper()) {
					// parse the port parameter
					case "-PORT":
					case "-P":
					case "--PORT":
						if (i + 1 < cmdArgs.Length) {
							try {
								port = int.Parse(cmdArgs[i + 1]);
							}
							// catch exceptions if the user enters a non integer values
							catch (FormatException) {
								LogWarning("The port number provided needs to be an integer between 1025 and 65535");
								return false;
							}
							// validate the port entered is within the correct range. 
							if ((port < 1025) || (port > 65535)) {
								LogWarning("The port number must be an integer between 1025 and 65535");
								return false;
							}
						}
						else {
							Usage();
							return false;
						}
						break;
					//  parse the filepath parameter
					case "-FILEPATH":
					case "--FILEPATH":
					case "-F":
						if (i + 1 < cmdArgs.Length) {
							filePath = cmdArgs[i + 1];
							//  validate the the directory exists
							if (!System.IO.Directory.Exists(filePath)) {
								LogWarning("The directory " + filePath + " does not exist");
								return false;
							}
							// append trailing slash if not present
							if (!(filePath.Substring(filePath.Length - 1, 1) == Path.DirectorySeparatorChar.ToString())) {
								filePath = filePath + Path.DirectorySeparatorChar.ToString();
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
						if (i + 1 < cmdArgs.Length) {
							string[] temp = cmdArgs[i + 1].Split(':');
							if (temp.Length == 2) {
								passthruHost = temp[0];
								try {
									passthruPort = int.Parse(temp[1]);
								}
								catch (FormatException) {
									LogWarning("The port number provided needs to be an integer between 1 and 65535");
									return false;
								}
								if ((passthruPort == 0) || (passthruPort > 65535)) {
									LogWarning("The port number provided needs to be an integer between 1 and 65535");
									return false;
								}
							}
							else {
								LogWarning("The passthru host and IP where not provided. eg -passthru host:port");
								return false;
							}
						}
						break;
					// set the text encoding method for the received message
					case "-ENCODING":
					case "--ENCODING":
					case "-E":
						if (i + 1 < cmdArgs.Length) {
							switch (cmdArgs[i + 1].ToUpper()) {
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
					// require TLS for connections from clients
					case "-TLS":
					case "--TLS":
					case "-S":
						LogInformation("TLS option detected");
						if (i + 1 < cmdArgs.Length) {
							// check for SHA1 certificate thumbprint
							if (Regex.IsMatch(cmdArgs[i + 1], "^[A-Z0-9]{40}$", RegexOptions.IgnoreCase)) {
								LogInformation("Certificate thumbprint detected - " + cmdArgs[i + 1]);
								// exit if a cert store certificate thumbprint was provided in a non Windows platform
								if (Environment.OSVersion.Platform != System.PlatformID.Win32NT) {
									LogWarning("Using a certificate from the certificate store is only supported on a Windows platform.");
									return false;
								}
								useTLS = true;
								string tlsCertificateThumbprint = cmdArgs[i + 1];
								certificate = GetCertificateFromCertStore(tlsCertificateThumbprint);
								if (certificate == null) {
										LogWarning("An error occurred while attempting read the certificate from the Windows cert store");
										LogWarning("Make sure the certificate thumbprint is correct.");
										return false;
								}
							}
							// otherwise assume a file path has been provided, check that it is a valid file
							else {
								LogInformation("Certificate filename detected - " + cmdArgs[i + 1]);
								if (!System.IO.File.Exists(cmdArgs[i + 1])) {
									LogWarning("The certificate file " + cmdArgs[i + 1] + " does not exist.");
									return false;
								}
								else {
									useTLS = true;
									string tlsCertificatePath = cmdArgs[i + 1];
									SecureString certPassword = GetPassword();
									certificate = GetCertificateFromFile(tlsCertificatePath, certPassword);
									if (certificate == null) {
										LogWarning("An error occurred while attempting import the PFX certificate " + tlsCertificatePath);
										LogWarning("Make sure the certificate is in PFX format and password is correct.");
										return false;
									}
								}
							}
						}
						// exit if no certificate path, or certificate thumbprint was found
						else {
							LogWarning("The path to the server TLS certificate, or the certificate thumbprint, was not provided. e.g. -TLS c:\\server.pfx");
							return false;
						}
						break;
				}
			}
			return true;
		}

		/// <summary>
		/// Construct a x509 certificate from a file. Return null if certificate could not be created.
		/// </summary>
		private static X509Certificate2 GetCertificateFromFile(string certFilePath, SecureString certPassword) {
			try {
				X509Certificate2 cert = new X509Certificate2(certFilePath, certPassword);
				return cert;
			}
			catch (Exception e) {
				LogWarning(e.Message);
				return null;
			}
		}

		/// <summary>
		/// obtain a x509 certificate from the Windows CertStore matching a SHA1 thumbprint . 
		/// </summary>
		private static X509Certificate2 GetCertificateFromCertStore(string Thumbprint) {
			try {
				X509Certificate2Collection CertCollection;
				// try local machine store first
				X509Store lmCertStore = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            	lmCertStore.Open(OpenFlags.ReadOnly);
            	CertCollection = lmCertStore.Certificates.Find(X509FindType.FindByThumbprint, Thumbprint, false);
            	lmCertStore.Close();
            
				// try the current user store if no certs found in local machine store
				if (0 == CertCollection.Count) {
        			X509Store cuCertStore = new X509Store(StoreName.My, StoreLocation.CurrentUser);
					cuCertStore.Open(OpenFlags.ReadOnly);
					CertCollection = cuCertStore.Certificates.Find(X509FindType.FindByThumbprint, Thumbprint, false);
					cuCertStore.Close();

					// no certs found in either store, return null
					if (0 == CertCollection.Count) {
						return null;
					}
				}
				// return the first cert found - searching on thumbprint so chance of thumbprint collision is low
				return CertCollection[0];		
			}
			catch (Exception e)  {
				LogWarning(e.Message);
				return null;
			}
		}

		/// <summary>
		/// read a secure string (password) from the console. 
		/// </summary>
		private static SecureString GetPassword() {
			// prompt user to enter password
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("");
			Console.WriteLine("Enter password for certificate (enter for no password):");
			Console.ResetColor();

			// read password entered into a secure string
			SecureString password = new SecureString();
			while (true) {
				ConsoleKeyInfo info = Console.ReadKey(true);
				if (info.Key == ConsoleKey.Enter) {
					Console.WriteLine("");
					break;
				}
				else if (info.Key == ConsoleKey.Backspace) {
					if (password.Length > 0) {
						password.RemoveAt(password.Length - 1);
						Console.Write("\b \b");
					}
				}
				// KeyChar == '\u0000' if the key pressed does not correspond to a printable character
				else if (info.KeyChar != '\u0000') {
					password.AppendChar(info.KeyChar);
					Console.Write("*");
				}
			}
			return password;
		}


		/// <summary>
		/// Display help on command line parameters
		/// </summary>
		private static void Usage() {
			Console.WriteLine("");
			Console.WriteLine(" HL7Listener - v1.4 - Robert Holme. A simple MLLP listener to archive HL7 messages to disk.");
			Console.WriteLine("");
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine(" HL7Listener.exe --Port <PortNumber> [--FilePath <path>] [--NoACK] [--Passthru <host>:<port>] [--TLS] <certificate-path>");
			Console.ResetColor();
			Console.WriteLine("");
			Console.WriteLine("    --Port <PortNumber> specifies the port to listen on. Must be an integer between 1025 and 65535");
			Console.WriteLine("");
			Console.WriteLine("    --FilePath <Path> The path to archive the received messages to. If no path is supplied, messages will be saved");
			Console.WriteLine("        to the directory the application is launched from.");
			Console.WriteLine("");
			Console.WriteLine("    --NoACK prevents ACKs from being sent. Without this switch ACKs will always be sent, even if not requested in MSH-15.");
			Console.WriteLine("");
			Console.WriteLine("    --Passthru <host>:<port> Pass all messages received through to the remote host. eg --Passthru somehost:5000");
			Console.WriteLine("");
			Console.WriteLine("    --Encoding <UTF8|Latin1|ASCII> Define the text encoding for received messages. Defaults to system default (UTF8).");
			Console.WriteLine("");
			Console.WriteLine("    --TLS <certificate.pfx> | <thumbprint> Require for TLS protected connections.");
			Console.WriteLine("         Provide server certificate (PFX format), or a thumbprint for cert from the Windows CertStore");
			Console.WriteLine("");
		}

		/// <summary>
		/// Write a warning message to the console
		/// </summary>
		/// <param name="message"></param>
		private static void LogWarning(string message) {
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("WARNING: " + message);
			Console.ResetColor();
		}


		private static void LogInformation(string message) {
			Console.WriteLine("INFO: " + message);
		}
	}
}
