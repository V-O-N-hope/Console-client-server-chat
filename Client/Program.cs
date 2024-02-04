using System.Net.Sockets;

string host = InputServerIPAddress();
int port = InputPort();
using TcpClient client = new TcpClient();
Console.Write("Введите свое имя: ");
string? userName = Console.ReadLine();
StreamReader? Reader = null;
StreamWriter? Writer = null;

try
{
    client.Connect(host, port); //подключение клиента
    Reader = new StreamReader(client.GetStream());
    Writer = new StreamWriter(client.GetStream());
    if (Writer is null || Reader is null) return;
    // запускаем новый поток для получения данных
    await Writer.WriteLineAsync(userName);
    await Writer.FlushAsync();

    string temp = await Reader.ReadLineAsync();

    if (temp == "connection_exists")
    {
        Console.WriteLine($"Добро пожаловать, {userName}");
        Task.Run(() => ReceiveMessageAsync(Reader));
        // запускаем ввод сообщений
        await SendMessageAsync(Writer);
    }
    else if (temp == "name_was_taken")
    {
        Console.WriteLine("Sorry, but this name was taken");
        Console.ReadLine();
    }


}
catch (Exception ex)
{
    Console.WriteLine(ex.Message);
}
Writer?.Close();
Reader?.Close();

async Task SendToServer(string message)
{
    await Writer.WriteLineAsync(message);
    await Writer.FlushAsync();
}

async Task SendFileToServer()
{
    string filePath;
    do
    {
        Console.WriteLine("Enter the path to the file:");
        filePath = Console.ReadLine();
    } while (!File.Exists(filePath));
    string fileName = Path.GetFileName(filePath);
    string extension = Path.GetExtension(filePath);
    if (extension.ToLower() == ".bin" || extension.ToLower() == ".exe" || extension.ToLower() == ".cmd")
    {
        Console.WriteLine($"Sorry, but this file extension is not available - {extension}");
    }
    else
    {


        long fileSize = new FileInfo(filePath).Length;

        // Отправляем на сервер команду для загрузки файла
        await SendToServer($"UPLOAD {fileName}");

        // Открываем поток на чтение файла
        using (FileStream fileStream = File.OpenRead(filePath))
        {
            byte[] buffer = new byte[1024];
            int bytesRead;
            long bytesSent = 0;
            //Stream stream = Writer.BaseStream;

            // Читаем и отправляем файл побайтно
            while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                await Writer.BaseStream.WriteAsync(buffer, 0, bytesRead);
                bytesSent += bytesRead;
                int progress = (int)(bytesSent * 100 / fileSize);
                int left = Console.CursorLeft;
                //Console.SetCursorPosition(left, Console.CursorTop);
                Console.Write($"\rSending file... {progress}%");
            }
            await Writer.FlushAsync();
            Console.WriteLine();
        }
    }
}

// отправка сообщений
async Task SendMessageAsync(StreamWriter writer)
{
    Console.WriteLine("Для отправки сообщений введите сообщение и нажмите Enter");
    Console.WriteLine("Для помощи напишите /help");
    while (true)
    {
        Console.Write("> ");
        string? message = Console.ReadLine();
        if (string.IsNullOrEmpty(message)) continue;
        if (message == "/help")
        {
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine("ALL - send to all\n" +
                              "USERS - show list of users\n" +
                              "toUser <userName> <message> - sends message to user\n" +
                              "UPLOAD FILE - upload file to server\n" +
                              "SEND FILE <fileName> <recipientName> - for sending file from srever to client");
            Console.ForegroundColor = ConsoleColor.White;

        }
        else if (message == "ALL")
        {
            message = null;
            Console.WriteLine("Input your message: ");
            while (message == null)
            {
                message = Console.ReadLine();
            }
            await SendToServer("GEN " + message);
        }
        else if (message == "USERS")
        {
            await SendToServer("USERS");
        }
        else if (message.StartsWith("toUser "))
        {
            await SendToServer(message);
        }
        else if (message == "UPLOAD FILE")
        {
            await SendFileToServer();
        }
        else if (message.StartsWith("SEND FILE"))
        {
            await SendToServer(message);
        }
        else if (message == "QUIT")
        {
            await SendToServer(message);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Incorrect command.");
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}

async Task RecieveFile(string? message)
{
    string[] parts = message.Split(' ');
    string fileName = parts[2];
    string directory = $"{userName}/received";

    // создаем директорию, если ее нет
    if (!Directory.Exists(directory))
        Directory.CreateDirectory(directory);

    // создаем FileStream для записи полученного файла
    using (FileStream fileStream = new FileStream(Path.Combine(directory, fileName), FileMode.Create, FileAccess.Write))
    {
        byte[] buffer = new byte[1024];
        int bytesRead;

        // читаем байты файла из NetworkStream и записываем в FileStream
        while ((bytesRead = await Reader.BaseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead);
            if (bytesRead < 1024)
            {
                break;
            }
        }
    }
    Console.WriteLine($"File {fileName} received and saved to {directory}");
}
// получение сообщений
async Task ReceiveMessageAsync(StreamReader reader)
{
    while (true)
    {
        try
        {
            // считываем ответ в виде строки
            string? message = await reader.ReadLineAsync();
            // если пустой ответ, ничего не выводим на консоль
            if (string.IsNullOrEmpty(message)) continue;
            else if (message.StartsWith("Receiving file"))
            {
                await RecieveFile(message);
            }
            else
                Print(message);//вывод сообщения
        }
        catch
        {
            break;
        }
    }
}
// чтобы полученное сообщение не накладывалось на ввод нового сообщения
void Print(string message)
{
    if (OperatingSystem.IsWindows())    // если ОС Windows
    {
        var position = Console.GetCursorPosition(); // получаем текущую позицию курсора
        int left = position.Left;   // смещение в символах относительно левого края
        int top = position.Top;     // смещение в строках относительно верха
                                    // копируем ранее введенные символы в строке на следующую строку
        Console.MoveBufferArea(0, top, left, 1, 0, top + 1);
        // устанавливаем курсор в начало текущей строки
        Console.SetCursorPosition(0, top);
        // в текущей строке выводит полученное сообщение
        Console.ForegroundColor = message.StartsWith("error_") ? ConsoleColor.DarkRed : ConsoleColor.DarkGreen;
        Console.WriteLine(message);
        Console.ForegroundColor = ConsoleColor.White;
        // переносим курсор на следующую строку
        // и пользователь продолжает ввод уже на следующей строке
        Console.SetCursorPosition(left, top + 1);
    }
    else Console.WriteLine(message);
}

bool isValidIPv4(string ipString)
{
    bool result = false;
    string[] splitValues = ipString.Split('.');

    if (splitValues.Length == 4)
    {
        result = splitValues.All(r => byte.TryParse(r, out byte tempForParsing));
    }
    return result;
}



string InputServerIPAddress()
{
    string inputString;
    bool isValid = false;
    Console.WriteLine("Введите Ip адрес сервера: ");
    do
    {
        inputString = Console.ReadLine();
        if (isValidIPv4(inputString))
        {
            isValid = true;
        }
        else
        {
            Console.WriteLine("Введите Ip адресс сервера в формате IPv4: ");
        }
    } while (!isValid);
    return inputString;
}

int InputPort()
{
    int number;
    Console.WriteLine("Введите порт: ");
    while (true)
    {

        string? input = Console.ReadLine();
        if (int.TryParse(input, out number) && number >= 0 && number <= 65536)
        {
            break;
        }
        Console.WriteLine("Некорректный ввод. Попробуйте еще раз.");
    }
    return number;
}