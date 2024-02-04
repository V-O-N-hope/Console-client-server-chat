using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

int port = GetAvaliablePort();
ServerObject server = new ServerObject(port);// создаем сервер
await server.ListenAsync(); // запускаем сервер

bool IsListeningPortAvailable(int port) =>
            !IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == port);

int GetAvaliablePort()
{
    string? inputString;
    int port;
    bool isValid = false;
    Console.WriteLine("Введите свободный порт для сервера: ");
    do
    {
        inputString = Console.ReadLine();
        if (int.TryParse(inputString, out port))
        {
            if (IsListeningPortAvailable(port))
            {
                isValid = true;
            }
            else
            {
                Console.WriteLine("Этот порт занят, повторите ввод.");
            }
        }
        else
        {
            Console.WriteLine("Порт сервера это целое число, повторите ввод.");
        }
    } while (!isValid);
    return port;
}


class ServerObject
{
    int port;
    TcpListener tcpListener; // сервер для прослушивания
    public List<ClientObject> clients;

    public ServerObject(int _port)
    {
        port = _port;
        tcpListener = new TcpListener(IPAddress.Any, port);
        clients = new List<ClientObject>();
    }

    
    protected internal void RemoveConnection(string id)
    {
        // получаем по id закрытое подключение
        ClientObject? client = clients.FirstOrDefault(c => c.Id == id);
        // и удаляем его из списка подключений
        if (client != null) clients.Remove(client);
        client?.Close();
    }

    protected internal bool isNameTaken(string name)
    {
        foreach (var client in clients)
        {
            if (name == client._name)
            {
                return true;
            }
        }
        return false;
    }

    protected internal string GetUsers(string? name)
    {
        StringBuilder sb = new StringBuilder();
        foreach (var client in clients)
        {
            if (client._name != name)
            {
                sb.AppendLine(client._name);
            }
            else
            {
                sb.AppendLine(client._name + " - you");
            }

        }
        return sb.ToString();
    }
    public static string[] GetLocalIPv4(NetworkInterfaceType _type)
    {
        List<string> ipAddrList = new List<string>();
        foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (item.NetworkInterfaceType == _type && item.OperationalStatus == OperationalStatus.Up)
            {
                foreach (UnicastIPAddressInformation ip in item.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ipAddrList.Add(ip.Address.ToString());
                    }
                }
            }
        }
        return ipAddrList.ToArray();
    }

    // прослушивание входящих подключений
    protected internal async Task ListenAsync()
    {
        try
        {
            tcpListener.Start();
            Console.WriteLine("Сервер запущен. Ожидание подключений...");
            int port = ((IPEndPoint) tcpListener.LocalEndpoint).Port;
            Console.WriteLine($"{port} - port");
            Console.WriteLine($"{GetLocalIPv4(NetworkInterfaceType.Wireless80211).FirstOrDefault()}");


            while (true)
            {
                TcpClient tcpClient = await tcpListener.AcceptTcpClientAsync();
                ClientObject clientObject = new ClientObject(tcpClient, this);
                string? temp = await clientObject.Reader.ReadLineAsync();
                if (temp != null && !isNameTaken(temp))
                {
                    clients.Add(clientObject);
                    clientObject._name = temp;
                    await clientObject.Writer.WriteLineAsync("connection_exists");
                    await clientObject.Writer.FlushAsync();
                    Task.Run(clientObject.ProcessAsync);
                }
                else
                {
                    await clientObject.Writer.WriteLineAsync("name_was_taken");
                    await clientObject.Writer.FlushAsync();
                    clientObject?.Close();
                    tcpClient?.Close();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        finally
        {
            Disconnect();
        }
    }

    // трансляция сообщения подключенным клиентам
    protected internal async Task BroadcastMessageAsync(string? message, string? name)
    {
        foreach (var client in clients)
        {
            if (client._name != name) // если id клиента не равно id отправителя
            {
                await client.Writer.WriteLineAsync(message); //передача данных
                await client.Writer.FlushAsync();
            }
        }
    }

    protected internal async Task ToUserMessage(string? message, string? sender, string? recipient)
    {
        foreach (var client in clients)
        {
            if (client._name == recipient) // если id клиента равно id отправителя
            {
                await client.Writer.WriteLineAsync($"[{sender}] to [{recipient}]: {message}"); //передача данных
                await client.Writer.FlushAsync();
            }
        }
    }
    // отключение всех клиентов
    protected internal void Disconnect()
    {
        foreach (var client in clients)
        {
            client.Close(); //отключение клиента
        }
        tcpListener.Stop(); //остановка сервера
    }
}
class ClientObject
{
    protected internal string Id { get; } = Guid.NewGuid().ToString();
    protected internal string? _name = null;

    protected internal StreamWriter Writer { get; }
    protected internal StreamReader Reader { get; }

    TcpClient client;
    ServerObject server; // объект сервера

    public ClientObject(TcpClient tcpClient, ServerObject serverObject)
    {
        client = tcpClient;
        server = serverObject;
        // получаем NetworkStream для взаимодействия с сервером
        var stream = client.GetStream();
        // создаем StreamReader для чтения данных
        Reader = new StreamReader(stream);
        // создаем StreamWriter для отправки данных
        Writer = new StreamWriter(stream);
    }

    private async Task sendBrodcastHandler(string message)
    {
        StringBuilder sb = new StringBuilder();
        var messages = message.Split(' ');
        for (int i = 1; i < messages.Length; i++)
        {
            sb.Append(messages[i] + ' ');
        }
        message = sb.ToString();
        sb.Clear();
        message = $"[{_name}]: {message}";
        Console.WriteLine(message);
        await server.BroadcastMessageAsync(message, _name);
        sb.Clear();
    }

    private string userMessageHandler(string message)
    {
        var messages = message.Split(' ');
        if (messages.Length < 3)
        {
            return "error_comand";
        }
        else
        {
            if (server.isNameTaken(messages[1]))
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 1; i < messages.Length; i++)
                {
                    sb.Append(messages[i] + ' ');
                }
                return sb.ToString();
            }
            else
                return "error_no_such_user";
        }
    }

    private async Task sendUserHandler(string? message)
    {
        message = userMessageHandler(message);
        if (message == "error_comand")
        {
            await Writer.WriteLineAsync(message);
            await Writer.FlushAsync();
        }
        else if (message == "error_no_such_user")
        {
            await Writer.WriteLineAsync(message);
            await Writer.FlushAsync();
        }
        else
        {
            var messages = message.Split(' ');
            StringBuilder sb = new StringBuilder();
            for (int i = 1; i < messages.Length; i++)
            {
                sb.Append(messages[i] + ' ');
            }
            await server.ToUserMessage(sb.ToString(), _name, messages[0]);
        }
    }

    private async Task uploadFile(string? message)
    {
        if (message.StartsWith("UPLOAD "))
        {
            string[] parts = message.Split(' ');
            if (parts.Length < 2)
            {
                await Writer.WriteLineAsync("Invalid UPLOAD command.");
                await Writer.FlushAsync();

            }
            else
            {
                string filename = parts[1];
                string filePath = Path.Combine($"{_name}/", filename);
                string extension = Path.GetExtension(filePath);
                if (!Directory.Exists($"{_name}"))
                {
                    Directory.CreateDirectory($"{_name}");
                }
                if (File.Exists(filePath))
                {
                    await Writer.WriteLineAsync($"File {filename} already exists.");
                    await Writer.FlushAsync();
                }
                if (extension.ToLower() == ".bin" || extension.ToLower() == ".exe" || extension.ToLower() == ".cmd")
                {
                    await Writer.WriteLineAsync($"Sorry, but this file extension is not available - {extension}");
                    await Writer.FlushAsync();
                }
                else
                {
                    try
                    {
                        using (var fileStream = new FileStream(filePath, FileMode.CreateNew))
                        {
                            byte[] buffer = new byte[1024];
                            int bytesRead;
                            while ((bytesRead = await Reader.BaseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                if (bytesRead < 1024)
                                {
                                    break;
                                }
                            }
                        }
                        await Writer.WriteLineAsync($"File {filename} uploaded successfully.");
                        await Writer.FlushAsync();
                    }
                    catch (IOException ex)
                    {
                        await Writer.WriteLineAsync($"Error uploading file: {ex.Message}");
                        await Writer.FlushAsync();
                    }
                }
            }
        }

    }

    private async Task SendFile(string? message)
    {
        string[] parts = message.Split(' ');
        if (parts.Length != 4)
        {
            await Writer.WriteLineAsync("Invalid SEND FILE command.");
            await Writer.FlushAsync();
            
        }
        else
        {
            string filePath = $"{_name}/{Path.GetFileName(parts[2])}";
            string recipientName = parts[3];

            // Проверяем, существует ли файл
            if (!File.Exists(filePath))
            {
                await Writer.WriteLineAsync($"File {filePath} does not exist on the server.");
                await Writer.FlushAsync();
                
            }
            else
            {
                ClientObject? recipientClient = null;
                if (server.isNameTaken(recipientName))
                {
                    foreach (var client in server.clients)
                    {
                        if (recipientName == client._name)
                        {
                            recipientClient = client;
                        }
                    }
                    try
                    {
                        await recipientClient.Writer.WriteLineAsync($"Receiving file {Path.GetFileName(filePath)} from {_name}...");
                        await recipientClient.Writer.FlushAsync();

                        // Открываем файл для чтения и отправляем его клиенту
                        using (var fileStream = new FileStream(filePath, FileMode.Open))
                        {
                            byte[] buffer = new byte[1024];
                            int bytesRead;
                            while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await recipientClient.Writer.BaseStream.WriteAsync(buffer, 0, bytesRead);
                            }
                        }

                        // Отправляем сообщение клиенту об успешной передаче файла
                        await recipientClient.Writer.WriteLineAsync($"File {Path.GetFileName(filePath)} received from {_name}.");
                        await recipientClient.Writer.FlushAsync();
                    }
                    catch
                    {
                        await Writer.WriteLineAsync("Something happend file transfering file");
                        await Writer.FlushAsync();
                    }
                }
                else
                {
                    await Writer.WriteLineAsync($"Client {recipientName} is not connected.");
                    await Writer.FlushAsync();
                }   
                
            }
        }
    }
    public async Task ProcessAsync()
    {
        try
        {
            string? message = $"{_name} вошел в чат";
            // посылаем сообщение о входе в чат всем подключенным пользователям
            await server.BroadcastMessageAsync(message, _name);
            Console.WriteLine(message);
            // в бесконечном цикле получаем сообщения от клиента
            while (true)
            {
                try
                {
                    message = await Reader.ReadLineAsync();
                    if (message == null) continue;
                    else if (message.StartsWith("GEN "))
                    {
                        await sendBrodcastHandler(message);
                    }
                    else if (message == "USERS")
                    {
                        message = server.GetUsers(_name);
                        await Writer.WriteLineAsync(message);
                        await Writer.FlushAsync();
                    }
                    else if (message.StartsWith("toUser "))
                    {
                        await sendUserHandler(message);
                    }
                    else if (message.StartsWith("UPLOAD ")){
                        await uploadFile(message);
                    }
                    else if (message.StartsWith("SEND FILE")){
                        await SendFile(message);
                    }
                    else if (message == "QUIT")
                    {
                        await Writer.WriteLineAsync($"Bye, dear {_name}");
                        throw new Exception();
                    }
                }
                catch
                {
                    message = $"{_name} покинул чат";
                    Console.WriteLine(message);
                    await server.BroadcastMessageAsync(message, _name);
                    server.RemoveConnection(Id);
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
        finally
        {
            // в случае выхода из цикла закрываем ресурсы
            server.RemoveConnection(Id);
        }
    }
    // закрытие подключения
    protected internal void Close()
    {
        Writer.Close();
        Reader.Close();
        client.Close();
    }
}