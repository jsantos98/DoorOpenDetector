using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Net.Security;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using System.Text.Json;
using DoorOpenDetector.Objects;
using ntfy;
using ntfy.Requests;
using System.Diagnostics;
using MQTTnet.Server;

namespace DoorOpenDetector
{
   public class DoorDetector
   {
      private bool bRead = true;
      private bool bPirActivated = false;
      private bool _lastOpenState = false;
      private DateTime _openTime = DateTime.MinValue;

      private Client _client;
      private string _url = "https://ntfy.sh";
      private string _topic = "dooropen-jsantos98";

      public DoorDetector()
      {
      }

      public void Connect()
      {
         _client = new Client(this._url);
      }



      public async Task Start()
      {
         // SendEmail("teste", "Hello");


         var mqttFactory = new MqttFactory();
         using (var mqttClient = mqttFactory.CreateMqttClient())
         {
            var mqttClientOptions = new MqttClientOptionsBuilder()
               .WithTcpServer("192.168.1.19")
               .WithCredentials("admin", "admin")
               .Build();
            // Setup message handling before connecting so that queued messages
            // are also handled properly. When there is no event handler attached all
            // received messages get lost.
            mqttClient.ApplicationMessageReceivedAsync += e =>
            {
               var payload = ASCIIEncoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
               switch (e.ApplicationMessage.Topic)
               {
                  case "tele/tasmota_880918/2838/SENSOR": this.HandleHallSensor(payload); break;
                  case "tele/tasmota_880918/BD8C/SENSOR": this.HandlePIRSensor(payload); break;
               }

               return Task.CompletedTask;
            };

            var mqttSubscribeOptions = mqttFactory.CreateSubscribeOptionsBuilder()
                .WithTopicFilter(f => { f.WithTopic("tele/tasmota_880918/2838/SENSOR"); })
                .WithTopicFilter(f => { f.WithTopic("tele/tasmota_880918/BD8C/SENSOR"); })
                .Build();

            mqttClient.DisconnectedAsync += async e =>
            {
               Console.WriteLine("Disconnected...");
               if (e.ClientWasConnected)
               {
                  await mqttClient.ConnectAsync(mqttClientOptions, CancellationToken.None);
                  await mqttClient.SubscribeAsync(mqttSubscribeOptions, CancellationToken.None);
                  Console.WriteLine("Connected...");
               }
            };

            await mqttClient.ConnectAsync(mqttClientOptions, CancellationToken.None);
            var x = await mqttClient.SubscribeAsync(mqttSubscribeOptions, CancellationToken.None);
            Console.WriteLine("Connected...");

            await this.StartHeartbeat(mqttClient, mqttClientOptions, mqttSubscribeOptions);

            Console.WriteLine("MQTT client subscribed to topic.");
   
            while (true)
            {
               Thread.Sleep(1000);
            }

            Console.WriteLine("Press enter to exit.");
            Console.ReadLine();
         }
      }

      private async Task StartHeartbeat(IMqttClient client, MqttClientOptions mqttClientOptions, MqttClientSubscribeOptions mqttSubscribeOptions)
      {
         _ = Task.Run(async () =>
         {
            // User proper cancellation and no while(true).
            int counter = 2000;
            while (true)
            {
               if (++counter > 90)
               {
                  Console.Write($"\r\n{DateTime.Now.ToString("HH:mm:ss")}: ");
                  counter = 0;
               }

               try
               {
                  // This code will also do the very first connect! So no call to _ConnectAsync_ is required in the first place.
                  if (!await client.TryPingAsync())
                  {
                     Console.Write("#");

                     //await mqttClient.ConnectAsync(mqttClientOptions, CancellationToken.None);

                     await client.ConnectAsync(mqttClientOptions, CancellationToken.None);
                     await client.SubscribeAsync(mqttSubscribeOptions, CancellationToken.None);
                     Console.WriteLine("RECONNECTED...");


                     //// Subscribe to topics when session is clean etc.
                     //Console.WriteLine("The MQTT client is connected.");
                  }
                  else
                  {
                     Console.Write(".");
                  }
               }
               catch (Exception ex)
               {
                  // Handle the exception properly (logging etc.).
                  Console.WriteLine(ex.Message);
               }
               finally
               {
                 

                  // Check the connection state every 5 seconds and perform a reconnect if required.
                  await Task.Delay(TimeSpan.FromSeconds(10));
               }
            }
         });
      }

      private void HandleHallSensor(string payload)
      {
         var data = JsonSerializer.Deserialize<ZigBeePayload<HallSensor>>(payload)?.Get();

         if (data != null)
         {
            if (_lastOpenState != data.IsOpen)
            {
               _lastOpenState = data.IsOpen;
               if (!data.IsOpen)
               {
                  Console.WriteLine($"\nDetected door close at '{DateTime.Now.ToString()}'");

                  TimeSpan tsDuration = DateTime.Now - _openTime;
                  // if (tsDuration.TotalSeconds > 4.0d)
                  {
                     string mode = "abertura";
                     if (bPirActivated)
                     {
                        mode = "entrada";
                     }

                     string strMessage = string.Format("\nDetectada {4} às {0} durante {1} segundos ({2}, {3})", _openTime.ToShortTimeString(), (int)tsDuration.TotalSeconds, _openTime.ToShortDateString(), _openTime.DayOfWeek, mode);
                     Console.WriteLine(strMessage);
                     File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Occurrences.txt"), string.Concat(strMessage, "\r\n"));
                     var last = File.ReadAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Occurrences.txt")).ToList().TakeLast(10);

                     this.SendEmail(strMessage, string.Join("\r\n", last));
                     this.SendNotification("Door Usage", "house", strMessage);
                  }
               }
               else
               {
                  _openTime = DateTime.Now;
                  bPirActivated = false;
                  Console.WriteLine($"\nDetected door open at '{_openTime.ToString()}'");
               }
            }
         }
      }

      public void HandlePIRSensor(string payload)
      {
         var data = JsonSerializer.Deserialize<ZigBeePayload<PirSensor>>(payload)?.Get();

         if (data != null && data.Occupancy == 1)
         {
            Console.WriteLine($"\n{DateTime.Now.ToString("HH:mm:ss")}: PIR: {data.Occupancy} -> {data.Occupancy ?? 0}");
            if (data.Occupancy == 1)
            {
               bPirActivated = true;
               // this.SendNotification("Door Usage", "raising_hand_man", $"Detectado movimento às '{DateTime.Now.ToString("HH:mm:ss")}'");
            }
         }
      }

      public void Stop()
      {
         this.bRead = false;
      }

      private void SendEmail(string subject, string body)
      {
         MailAddress fromAddress = new MailAddress("portacasa@atlaneon.com", "Porta de Casa");
         SmtpClient smtpClient = new SmtpClient()
         {
            Host = "smtp.gmail.com",
            Port = 587,
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(fromAddress.Address, "@PiresDeLima")
         };
         SmtpClient smtp = smtpClient;
         ServicePointManager.ServerCertificateValidationCallback = (object s, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => true;
         MailMessage mailMessage = new MailMessage()
         {
            Subject = subject.Trim(),
            Body = body
         };
         MailMessage message = mailMessage;
         message.From = fromAddress;
         message.To.Add(new MailAddress("jsantos98@gmail.com", "Joao"));
         //message.To.Add(new MailAddress("profmatlopes@gmail.com", "Carina"));
         smtp.Send(message);
      }

      private async Task SendNotification(string title, string tag, string body)
      {
         try
         {
            var message = new SendingMessage
            {
               Title = title,
               Tags = tag.Split(",").ToArray(),
               Message = body
            };

            await _client.Publish(_topic, message);
         }
         catch (Exception e)
         {
            Console.WriteLine("Failed to send notification '{0}'", title);
         }
      }
   }

}
