using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Proxy
{
    class Proxy
    {
        public const int backlog = 20;
        public const int bufferSize = 1024 * 20;

        public string[] blackList;        
        private IPAddress address;
        private int port; 
        private Socket socket;

        public Proxy(IPAddress ip, int port, string listName)
        {
            address = ip;
            this.port = port;
            blackList = GetBlackList(listName);            
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp); // новый сокет
            socket.Bind(new IPEndPoint(address, port)); // ибо конкретная локальная точка, из которой передаются data
        }

        private static string[] GetBlackList(string fileName)
        {
            string list = "";
            using (StreamReader reader = new StreamReader(fileName))
            {
                list = reader.ReadToEnd();
            }

            string[] blackList = list.Trim().Split(new char[] { '\n' });
            return blackList;
        }

        public void Start()
        {
            socket.Listen(backlog); // прослушка попыток соединения (кол-во соединений в очередь)
            while (true)
            {
                Socket newSocket = socket.Accept(); // извлекает первый ожидающий запрос и создает новый сокет
                Thread thread = new Thread(() => StartProxy(newSocket));
                thread.Start();
            }
        }

        public void StartProxy(Socket newSocket) 
        {
            NetworkStream networkStream = new NetworkStream(newSocket); // поток для получения и отправки данных
            string message = Encoding.UTF8.GetString(Receive(networkStream)); // преобразование полученных данных из
                                                                              // массива байтов в строку
            ProxyAnswer(networkStream, message);
            newSocket.Dispose();
        }

        public byte[] Receive(NetworkStream networkStream)
        {
            byte[] buffer = new byte[bufferSize];
            byte[] allData = new byte[bufferSize];
            int reciveBytes = 0;
            int size;

            do
            {
                size = networkStream.Read(buffer, 0, buffer.Length); // считывание данных из потока
                Array.Copy(buffer, 0, allData, reciveBytes, size); // копирование массива
                reciveBytes += size;
            } while (networkStream.DataAvailable && reciveBytes < bufferSize);

            return allData;
        }

        public void ProxyAnswer(NetworkStream networkStream, string message)
        {
            Socket server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                message = GetSitePath(message); // Находит запрос клиента без имени сайта
                string[] splitMessage = message.Split('\r', '\n'); // Разделяет построчно  
                string host = splitMessage.FirstOrDefault((str) => str.Contains("Host: ")); // искает имя хоста
                host = host.Remove(host.IndexOf("Host: "), ("Host: ").Length); // нормальное имя хоста

                if (blackList != null && Array.IndexOf(blackList, host.ToLower()) != -1)
                {
                    // Необходимые заголовки: ответ сервера, тип и длина содержимого. После двух пустых строк - 
                    // само содержимое
                    string error = $"HTTP/1.1 403 Forbidden\nContent-Type: text/html\r\nContent-Length: 40\n\nAccess denied. This syte is in blacklist";
                    byte[] errorpage = Encoding.UTF8.GetBytes(error);
                    networkStream.Write(errorpage, 0, errorpage.Length); // отправляет плохое сообщение
                    Console.WriteLine(DateTime.Now + ": " + host + " 403 (blocked)");
                    return;
                }

                string[] hostNameOrAddress = host.Split(':');
                IPAddress hostIP = Dns.GetHostEntry(hostNameOrAddress[0]).AddressList[0];
                IPEndPoint serverEP;
                serverEP = new IPEndPoint(hostIP, int.Parse(hostNameOrAddress[1]));

                server.Connect(serverEP); // устанавливает соединение
                NetworkStream serverStream = new NetworkStream(server); // поток для получения\отправки            
                byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                serverStream.Write(messageBytes, 0, messageBytes.Length);
                byte[] receiveData = Receive(serverStream);
                networkStream.Write(receiveData, 0, receiveData.Length);

                string code = GetAnswerCode(Encoding.UTF8.GetString(receiveData));
                Console.WriteLine(DateTime.Now.ToString() + " Host: {0} code: {1}", hostNameOrAddress[0], code);
                //serverStream.CopyTo(networkStream);
            }
            catch (Exception e)
            {
                return;
            }
            finally
            {
                server.Dispose();
            }
            

        }

        public string GetSitePath(string message) 
        {
            // искает http запрос клиента
            MatchCollection matchCollection = (new Regex(@"http:\/\/[a-z0-9а-я\.\:]*")).Matches(message);
            // нашел, берем первый
            string host = matchCollection[0].Value;
            message = message.Replace(host, "");
            return message;
        }

        public string GetAnswerCode(string serverResponse) 
        {
            string[] response = serverResponse.Split('\n');
            string code = response[0].Substring(response[0].IndexOf(" ") + 1);

            return code;
        }
    }
}
