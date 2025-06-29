﻿// Rob Holme (rob@holme.com.au)
// 03/06/2015 - Initial version

namespace HL7ListenerApplication {
	using System;
	using System.Text;
	using System.Net;
	using System.Net.Security;
	using System.Net.Sockets;
	using System.Threading;
	using System.Collections.Concurrent;
	using System.Security.Cryptography.X509Certificates;

	class HL7TCPListener {
		const int TCP_TIMEOUT = 300000; // timeout value for receiving TCP data in milliseconds
		private TcpListener tcpListener;
		private Thread tcpListenerThread;
		private Thread passthruThread;
		//		private Thread passthruAckThread;
		private int listenerPort;
		private string archivePath = null;
		private bool sendACK = true;
		private string passthruHost = null;
		private int passthruPort;
		private NetworkStream PassthruClientStream;
		private TcpClient passthruClient;
		private IPEndPoint remoteEndpoint;
		private ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();
		private bool runThread = true;
		private Encoding encoder = Encoding.Default;
		private bool tlsRequired = false;
		private X509Certificate2 certificate;
		private CancellationTokenSource srcCancelToken = new CancellationTokenSource();
		private bool debug = false;

		/// <summary>
		/// Constructor
		/// </summary>
		public HL7TCPListener(int port) {
			this.listenerPort = port;
			this.tlsRequired = false;
			this.certificate = null;
		}

		/// <summary>
		/// Constructor - define encoding
		/// </summary>
		public HL7TCPListener(int port, Encoding encoding) {
			this.listenerPort = port;
			this.encoder = encoding;
			this.certificate = null;
		}

		/// <summary>
		/// Constructor - supply certificate to use TLS
		/// </summary>
		public HL7TCPListener(int port, X509Certificate2 certificate) {
			this.listenerPort = port;
			this.tlsRequired = true;
			this.certificate = certificate;
		}

		/// <summary>
		/// Constructor - supply certificate to use TLS, define encoding
		/// </summary>
		public HL7TCPListener(int port, Encoding encoding, X509Certificate2 certificate) {
			this.listenerPort = port;
			this.encoder = encoding;
			this.tlsRequired = true;
			this.certificate = certificate;
		}

		/// <summary>
		/// Start the TCP listener. Log the options set.
		/// </summary>
		public bool Start() {
			CancellationToken cancelToken = this.srcCancelToken.Token;

			// start a new thread to listen for new TCP connections
			this.tcpListener = new TcpListener(IPAddress.Any, this.listenerPort);
			this.tcpListenerThread = new Thread(new ThreadStart(StartListener));
			this.tcpListenerThread.Start();
			LogImportant("Press 'ESC' to exit.");
			LogInformation("Starting HL7 listener on port " + this.listenerPort);
			LogInformation($"Message encoding: {this.encoder.EncodingName}");
			// log information to the console about the options provided by the user
			if (this.archivePath != null) {
				this.LogInformation("Archiving received messages to: " + this.archivePath);
			}
			else {
				this.LogInformation("Archiving received messages to current directory (--FilePath not detected).");
			}
			if (!sendACK) {
				this.LogInformation("Acknowledgements (ACKs) will not be sent");
			}
			this.LogInformation("TLS: " + this.tlsRequired);
			this.LogInformation("Message Debugging: " + this.debug);
			// if  a passthru host has been specified, create a new thread to send messages to the PassThru host
			if (this.passthruHost != null) {
				// create a connection to the Passthru host if the -PassThru option was specified.
				try {
					passthruClient = new TcpClient();
					remoteEndpoint = new IPEndPoint(IPAddress.Parse(this.PassthruHost), this.passthruPort);
					passthruClient.ConnectAsync(remoteEndpoint, cancelToken);
					PassthruClientStream = passthruClient.GetStream();
					PassthruClientStream.ReadTimeout = TCP_TIMEOUT;
					PassthruClientStream.WriteTimeout = TCP_TIMEOUT;
					this.LogInformation("Passing messages onto " + this.passthruHost + ":" + this.passthruPort);
				}
				// handle user cancel events
				catch (System.OperationCanceledException) {
					LogInformation("Cancel requested. Closing connection to PassThru host " + passthruHost + ":" + passthruPort);
					passthruClient.Close();
					passthruClient.Dispose();
					return true;
				}
				// report all other exceptions and exit
				catch (Exception e) {
					LogWarning("Unable to create connection to PassThru host " + passthruHost + ":" + passthruPort);
					LogWarning(e.Message);
					this.RequestStop();
					return false;
				}
				// create a thread to send messages to the Passsthru host 
				this.passthruThread = new Thread(new ThreadStart(SendData));
				passthruThread.Start();
				// create a thread to receive the ACKs from the passthru host
				//		this.passthruAckThread = new Thread(new ThreadStart(ReceiveACK));
				//			passthruAckThread.Start();
				LogInformation("Connected to PassThru host");
			}

			while (!Console.KeyAvailable) {
				ConsoleKeyInfo keyInfo = Console.ReadKey(true);
				if (keyInfo.Key == ConsoleKey.Escape) {
					LogInformation("Exiting.");
					this.RequestStop();
					return true;
				}
			}
			return true;
		}


		/// <summary>
		/// Stop the all threads
		/// </summary>
		public void RequestStop() {
			LogWarning("Cancelling all running threads.");
			// stop processing loops
			this.runThread = false;
			// cancel all async operations
			srcCancelToken.Cancel();
		}

		/// <summary>
		/// Start listening for new connections
		/// </summary>
		private void StartListener() {
			try {
				// create a cancellation token linked to the source cancellation token
				CancellationToken cancelToken = this.srcCancelToken.Token;
				Thread clientThread;

				this.tcpListener.Start();
				// run the thread unless a request to stop is received
				while (this.runThread) {
					TcpClient client = this.tcpListener.AcceptTcpClientAsync(cancelToken).Result;
					this.LogInformation("New client connection accepted from " + client.Client.RemoteEndPoint);
					if (this.tlsRequired) {
						clientThread = new Thread(new ParameterizedThreadStart(ReceiveTLSData));
					}
					else {
						clientThread = new Thread(new ParameterizedThreadStart(ReceiveData));
					}
					// start the client thread to wait for a client connection
					clientThread.Start(client);
				}
				this.tcpListener.Stop();
			}
			// the read was cancelled
			catch (System.OperationCanceledException) {
				LogInformation("Cancel requested. Closing TCP Listener on port " + this.listenerPort);
			}
			catch (Exception e) {
				LogWarning("An error occurred while attempting to start the listener on port " + this.listenerPort);
				LogWarning(e.Message);
				LogWarning("HL7Listener exiting.");
			}
		}


		/// <summary>
		/// Receive data from a client connection, look for MLLP HL7 message.
		/// </summary>
		/// <param name="client"></param>
		private void ReceiveData(object client) {
			// create a cancellation token linked to the source cancellation token
			CancellationToken cancelToken = this.srcCancelToken.Token;

			// generate a random sequence number to use for the file names
			Random random = new Random(Guid.NewGuid().GetHashCode());
			int filenameSequenceStart = random.Next(0, 1000000);

			TcpClient tcpClient = (TcpClient)client;
			NetworkStream clientStream;

			clientStream = tcpClient.GetStream();
			clientStream.ReadTimeout = TCP_TIMEOUT;
			clientStream.WriteTimeout = TCP_TIMEOUT;

			byte[] messageBuffer = new byte[4096];
			int bytesRead;
			String messageData = "";
			int messageCount = 0;

			while (this.runThread) {
				bytesRead = 0;
				try {
					// Wait until a client application submits a message
					bytesRead = clientStream.ReadAsync(messageBuffer, 0, 4096, cancelToken).Result;
				}
				// the read was cancelled
				catch (System.OperationCanceledException) {
					LogInformation("Cancel initiated. Closing connection to " + tcpClient.Client.RemoteEndPoint);
					break;
				}
				// A network error has occurred
				catch {
					LogInformation("Connection from " + tcpClient.Client.RemoteEndPoint + " has ended");
					break;
				}
				// The client has disconnected
				if (bytesRead == 0) {
					LogInformation("The client " + tcpClient.Client.RemoteEndPoint + " has disconnected");
					break;
				}
				// Message buffer received successfully
				messageData += encoder.GetString(messageBuffer, 0, bytesRead);

				// Log the message received if debug is enabled
				if (this.debug) {
					LogInformation("Received message\n: " + messageData);
				}

				// Find a VT character, this is the beginning of the MLLP frame
				int start = messageData.IndexOf((char)0x0B);
				if (start >= 0) {
					// Search for the end of the MLLP frame (a FS character)
					int end = messageData.IndexOf((char)0x1C);
					if (end > start) {
						messageCount++;
						try {
							// queue the message to sent to the passthru host if the -PassThru option has been set
							if (passthruHost != null) {
								messageQueue.Enqueue(messageData.Substring(start + 1, end - (start + 1)));
							}
							// create a HL7message object from the message received. Use this to access elements needed to populate the ACK message and file name of the archived message
							HL7Message message = new HL7Message(messageData.Substring(start + 1, end - (start + 1)));
							messageData = ""; // reset the message data string for the next message
							string messageTrigger = message.GetMessageTrigger();
							string messageControlID = message.GetHL7Item("MSH-10")[0];
							//string acceptAckType = message.GetHL7Item("MSH-15")[0];
							string dateStamp = DateTime.Now.ToString("yyyyMMddhhmm");
							string filename = dateStamp + "_" + (filenameSequenceStart + messageCount).ToString("D6") + "_" + messageTrigger + ".hl7"; //  increment sequence number for each filename
																																					   // Write the HL7 message to file.
							WriteMessageToFile(message.ToString(), this.archivePath + filename);
							// send ACK message is MSH-15 is set to AL and ACKs not disabled by -NOACK command line switch
							//if ((this.sendACK) && (acceptAckType.ToUpper() == "AL"))
							if (this.sendACK) {
								LogInformation("Sending ACK (Message Control ID: " + messageControlID + ")");
								// generate ACK Message and send in response to the message received
								string response = GenerateACK(message.ToString());  // TO DO: send ACKS if set in message header, or specified on command line
								byte[] encodedResponse = encoder.GetBytes(response);

								// Log the ACK message if debug is enabled
								if (this.debug) {
									LogInformation("ACK message sent:\n" + response);
								}

								// Send response
								try {
									clientStream.Write(encodedResponse, 0, encodedResponse.Length);
									clientStream.Flush();
								}
								catch (Exception e) {
									// A network error has occurred
									LogInformation("An error has occurred while sending an ACK to the client " + tcpClient.Client.RemoteEndPoint);
									LogInformation(e.Message);
									break;
								}
							}
						}
						catch (Exception e) {
							messageData = ""; // reset the message data string for the next message
							LogWarning("An exception occurred while parsing the HL7 message");
							LogWarning(e.Message);
							break;
						}
					}
				}
			}
			// close the stream and tcp client. Exit thread.
			try {
				LogInformation("Total messages received from " + tcpClient.Client.RemoteEndPoint + " - " + messageCount);
				clientStream.Close();
				clientStream.Dispose();
				tcpClient.Close();
				tcpClient.Dispose();
			}
			catch {
				// nothing to do.
			}
		}

		/// <summary>
		/// Receive data from a TLS client connection, look for MLLP HL7 message.
		/// </summary>
		/// <param name="client"></param>
		private void ReceiveTLSData(object client) {
			// create a cancellation token linked to the source cancellation token
			CancellationToken cancelToken = this.srcCancelToken.Token;

			// generate a random sequence number to use for the file names
			Random random = new Random(Guid.NewGuid().GetHashCode());
			int filenameSequenceStart = random.Next(0, 1000000);

			try {
				TcpClient tcpClient = (TcpClient)client;
				SslStream clientStream = new SslStream(tcpClient.GetStream());
				clientStream.ReadTimeout = TCP_TIMEOUT;
				clientStream.WriteTimeout = TCP_TIMEOUT;
				clientStream.AuthenticateAsServer(this.certificate);

				byte[] messageBuffer = new byte[4096];
				int bytesRead;
				String messageData = "";
				int messageCount = 0;

				while (this.runThread) {
					bytesRead = 0;
					try {
						// Wait until a client application submits a message
						bytesRead = clientStream.ReadAsync(messageBuffer, 0, 4096, cancelToken).Result;
					}
					catch (System.OperationCanceledException) {
						LogInformation("Cancel requested. Closing connection to " + tcpClient.Client.RemoteEndPoint);
						break;
					}
					catch {
						// A network error has occurred
						LogInformation("Connection from " + tcpClient.Client.RemoteEndPoint + " has ended");
						break;
					}
					if (bytesRead == 0) {
						// The client has disconnected
						LogInformation("The client " + tcpClient.Client.RemoteEndPoint + " has disconnected");
						break;
					}
					// Message buffer received successfully
					messageData += encoder.GetString(messageBuffer, 0, bytesRead);

					// Log the message received if debug is enabled
					if (this.debug) {
						LogInformation("Received message:\n: " + messageData);
					}


					// Find a VT character, this is the beginning of the MLLP frame
					int start = messageData.IndexOf((char)0x0B);
					if (start >= 0) {
						// Search for the end of the MLLP frame (a FS character)
						int end = messageData.IndexOf((char)0x1C);
						if (end > start) {
							messageCount++;
							try {
								// queue the message to sent to the passthru host if the -PassThru option has been set
								if (passthruHost != null) {
									messageQueue.Enqueue(messageData.Substring(start + 1, end - (start + 1)));
								}
								// create a HL7message object from the message received. Use this to access elements needed to populate the ACK message and file name of the archived message
								HL7Message message = new HL7Message(messageData.Substring(start + 1, end - (start + 1)));
								messageData = ""; // reset the message data string for the next message
								string messageTrigger = message.GetMessageTrigger();
								string messageControlID = message.GetHL7Item("MSH-10")[0];
								//string acceptAckType = message.GetHL7Item("MSH-15")[0];
								string dateStamp = DateTime.Now.ToString("yyyyMMddhhmm");
								string filename = dateStamp + "_" + (filenameSequenceStart + messageCount).ToString("D6") + "_" + messageTrigger + ".hl7"; //  increment sequence number for each filename
																																						   // Write the HL7 message to file.
								WriteMessageToFile(message.ToString(), this.archivePath + filename);
								// send ACK message is MSH-15 is set to AL and ACKs not disabled by -NOACK command line switch
								//if ((this.sendACK) && (acceptAckType.ToUpper() == "AL"))
								if (this.sendACK) {
									LogInformation("Sending ACK (Message Control ID: " + messageControlID + ")");
									// generate ACK Message and send in response to the message received
									string response = GenerateACK(message.ToString());  // TO DO: send ACKS if set in message header, or specified on command line
									byte[] encodedResponse = encoder.GetBytes(response);

									// Log the ACK message if debug is enabled
									if (this.debug) {
										LogInformation("ACK message sent:\n" + response);
									}

									// Send response
									try {
										clientStream.WriteAsync(encodedResponse, 0, encodedResponse.Length, cancelToken);
										clientStream.Flush();
									}
									catch (System.OperationCanceledException) {
										LogInformation("Cancel initiated. Terminating send of ACK for messageControlID: " + messageControlID);
										break;
									}
									catch (Exception e) {
										// A network error has occurred
										LogInformation("An error has occurred while sending an ACK to the client " + tcpClient.Client.RemoteEndPoint);
										LogInformation(e.Message);
										LogInformation(e.InnerException.ToString());
										break;
									}
								}
							}
							catch (Exception e) {
								messageData = ""; // reset the message data string for the next message
								LogWarning("An exception occurred while parsing the HL7 message");
								LogWarning(e.Message);
								break;
							}
						}
					}
					else {
						// No MLLP frame found, continue to read more data
						LogWarning("No MLLP frame found in message received.");
					}
				}
				LogInformation("Total messages received:" + messageCount);
				try {
					clientStream.Close();
					clientStream.Dispose();
					tcpClient.Close();
					tcpClient.Dispose();
				}
				catch {
					// nothing to do
				}
			}
			catch (Exception e) {
				LogWarning("An error occurred while attempting to negotiate TLS.");
				LogWarning(e.Message);
			}
		}


		/// <summary> 
		/// Send the HL7 message to the remote host in a MLLP frame
		/// </summary>
		/// <param name="ClientStream"></param>
		/// <param name="MessageData"></param>
		private void SendData() {
			// create a cancellation token linked to the source cancellation token
			CancellationToken cancelToken = this.srcCancelToken.Token;

			string tempMessage;

			while (this.runThread) {
				while (messageQueue.TryDequeue(out tempMessage)) {

					int bytesRead;
					string ackData = "";
					byte[] receiveBuffer = new byte[4096];


					// generate a MLLP framed message
					StringBuilder messageString = new StringBuilder();
					messageString.Append((char)0x0B);
					messageString.Append(tempMessage);
					messageString.Append((char)0x1C);
					messageString.Append((char)0x0D);

					try {
						// encode and send the message
						byte[] buffer = encoder.GetBytes(messageString.ToString().ToCharArray());

						// if the client connection has timed out, or the remote host has disconnected, reconnect.
						if (!this.PassthruClientStream.CanWrite) {
							LogInformation("Connection to passthru host has closed. Reconnecting to " + this.passthruHost + ":" + this.passthruPort);
							this.passthruClient.Close();
							this.passthruClient = new TcpClient();
							this.remoteEndpoint = new IPEndPoint(IPAddress.Parse(this.PassthruHost), this.passthruPort);
							this.passthruClient.ConnectAsync(remoteEndpoint, cancelToken);

							this.PassthruClientStream = passthruClient.GetStream();
							this.PassthruClientStream.ReadTimeout = TCP_TIMEOUT;
							this.PassthruClientStream.WriteTimeout = TCP_TIMEOUT;
						}
						LogInformation("Sending message to PassThru host " + this.passthruHost + ":" + this.passthruPort);
						this.PassthruClientStream.WriteAsync(buffer, 0, buffer.Length, cancelToken);
						bytesRead = this.PassthruClientStream.ReadAsync(receiveBuffer, 0, 4096, cancelToken).Result;
						ackData += Encoding.UTF8.GetString(receiveBuffer, 0, bytesRead);
						// Find a VT character, this is the beginning of the MLLP frame
						int start = ackData.IndexOf((char)0x0B);
						if (start >= 0) {
							// Search for the end of the MLLP frame (a FS character)
							int end = ackData.IndexOf((char)0x1C);
							if (end > start) {
								LogInformation("ACK received from -PassThru host");
								ackData = "";
							}
						}
					}
					// the connection or write was cancelled
					catch (System.OperationCanceledException) {
						LogInformation("Cancel initiated. Closing passthru connection to " + this.passthruClient.Client.RemoteEndPoint);
					}
					catch (Exception e) {
						LogWarning("Unable to send message to -Passsthru host (" + this.PassthruHost + ":" + this.passthruPort + ")");
						LogWarning(e.Message);
					}
				}
				Thread.Sleep(500);
			}
			// close passthru connections
			try {
				this.passthruClient.Close();
				this.passthruClient.Dispose();
				this.PassthruClientStream.Close();
				this.PassthruClientStream.Dispose();
			}
			catch {
				// no handler needed at this point as application is exiting.
			}
		}

		/// <summary>
		/// /// <summary>
		/// Write the HL7 message received to file. Optionally provide the file path, otherwise use the working directory.     
		/// </summary>
		/// <param name="message"></param>
		/// <param name="filePath"></param>
		private void WriteMessageToFile(string message, string filename) {
			// write the HL7 message to file
			try {
				LogInformation("Received message. Saving to file " + filename);
				System.IO.StreamWriter file = new System.IO.StreamWriter(filename);
				file.Write(message);
				file.Close();
			}
			catch (Exception e) {
				LogWarning("Failed to write file " + filename);
				LogWarning(e.Message);
			}
		}

		/// <summary>
		/// Generate a string containing the ACK message in response to the original message. Supply a string containing the original message (or at least the MSH segment).
		/// </summary>
		/// <returns></returns>
		string GenerateACK(string originalMessage) {
			// create a HL7Message object using the original message as the source to obtain details to reflect back in the ACK message
			HL7Message tmpMsg = new HL7Message(originalMessage);
			string trigger = tmpMsg.GetHL7Item("MSH-9.2")[0];
			string originatingApp = tmpMsg.GetHL7Item("MSH-3")[0];
			string originatingSite = tmpMsg.GetHL7Item("MSH-4")[0];
			string messageID = tmpMsg.GetHL7Item("MSH-10")[0];
			string processingID = tmpMsg.GetHL7Item("MSH-11")[0];
			string hl7Version = tmpMsg.GetHL7Item("MSH-12")[0];
			string ackTimestamp = DateTime.Now.ToString("yyyyMMddhhmm");

			StringBuilder ACKString = new StringBuilder();
			ACKString.Append((char)0x0B);
			ACKString.Append("MSH|^~\\&|HL7Listener|HL7Listener|" + originatingSite + "|" + originatingApp + "|" + ackTimestamp + "||ACK^" + trigger + "|" + messageID + "|" + processingID + "|" + hl7Version);
			ACKString.Append((char)0x0D);
			ACKString.Append("MSA|CA|" + messageID);
			ACKString.Append((char)0x1C);
			ACKString.Append((char)0x0D);
			return ACKString.ToString();
		}


		/// <summary>
		/// Receive ACKs from the PassThru host.
		/// Run this in a thread as this will block execution waiting for a response.
		/// </summary>
		/*		private void ReceiveACK() {
					// create a cancellation token linked to the source cancellation token
					CancellationToken cancelToken = this.srcCancelToken.Token;

					int bytesRead;
					string ackData = "";
					byte[] receiveBuffer = new byte[4096];

					// wait for the ACK to be returned, or a timeout occurs. Do nothing with the ACK received (discard).
					LogInformation("Receive ACK thread started");
					while (this.runThread) {
						try {
							bytesRead = this.PassthruClientStream.ReadAsync(receiveBuffer, 0, 4096, cancelToken).Result;
							// Message buffer received successfully
							ackData += Encoding.UTF8.GetString(receiveBuffer, 0, bytesRead);
							// Find a VT character, this is the beginning of the MLLP frame
							int start = ackData.IndexOf((char)0x0B);
							if (start >= 0) {
								// Search for the end of the MLLP frame (a FS character)
								int end = ackData.IndexOf((char)0x1C);
								if (end > start) {
									LogInformation("ACK received from -PassThru host");
									ackData = "";
								}
							}
						}
						catch (System.OperationCanceledException) {
							LogInformation("Cancel requested. Existing ReceiveACK");
						}
						catch (Exception) {
							LogWarning("Connection timed out or ended while waiting for ACK from PassThru host.");
							break;
						}
					}
				}
		*/
		/// <summary>
		/// Set and get the values of the SendACK option. This can be used to override sending of ACK messages. 
		/// </summary>
		public bool SendACK {
			get { return this.sendACK; }
			set { this.sendACK = value; }
		}

		/// <summary>
		/// The PassthruHost property identifies the host to pass the messages through to
		/// </summary>
		public string ArchivePath {
			set { this.archivePath = value; }
			get { return this.archivePath; }
		}

		/// <summary>
		/// The PassthruHost property identifies the host to pass the messages through to
		/// </summary>
		public string PassthruHost {
			set { this.passthruHost = value; }
			get { return this.passthruHost; }
		}

		/// <summary>
		/// The PassthruPort property identifies the remote port to pass the messages thought to.
		/// </summary>
		public int PassthruPort {
			set { this.passthruPort = value; }
			get { return this.passthruPort; }
		}

		/// <summary>
		/// Write informational event to the console.
		/// </summary>
		/// <param name="message"></param>
		private void LogInformation(string message) {
			Console.WriteLine(DateTime.Now + ": " + message);
		}

		/// <summary>
		/// Write a warning message to the console
		/// </summary>
		/// <param name="message"></param>
		private void LogWarning(string message) {
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("WARNING: " + message);
			Console.ResetColor();
		}

		/// <summary>
		/// Write a warning message to the console
		/// </summary>
		/// <param name="message"></param>
		private void LogImportant(string message) {
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("\n" + message + "\n");
			Console.ResetColor();
		}

		/// <summary>
		/// Set/Get the debug flag. If set to true, messages will be written to the console.
		/// </summary>
		public bool Debug {
			set { this.debug = value; }
			get { return this.debug; }
		}
	}

}
