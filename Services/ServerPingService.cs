namespace DevByteLauncher.Services;

public static class ServerPingService
{
    public static async Task<ServerStatusInfo> PingServerAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var result = new ServerStatusInfo
        {
            IsOnline = false
        };

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            using var tcpClient = new TcpClient();

            var sw = Stopwatch.StartNew();
            await tcpClient.ConnectAsync(host, port, linkedCts.Token);
            sw.Stop();
            result.LatencyMs = sw.ElapsedMilliseconds;

            using var stream = tcpClient.GetStream();

            using (var handshakePayload = new MemoryStream())
            {
                WriteVarInt(handshakePayload, 0x00);
                WriteVarInt(handshakePayload, 767);
                WriteString(handshakePayload, host);
                WriteShort(handshakePayload, (short)port);
                WriteVarInt(handshakePayload, 1);

                var payloadBytes = handshakePayload.ToArray();
                using var packet = new MemoryStream();
                WriteVarInt(packet, payloadBytes.Length);
                packet.Write(payloadBytes, 0, payloadBytes.Length);

                await stream.WriteAsync(packet.ToArray(), linkedCts.Token);
            }

            using (var requestPayload = new MemoryStream())
            {
                WriteVarInt(requestPayload, 0x00);

                var payloadBytes = requestPayload.ToArray();
                using var packet = new MemoryStream();
                WriteVarInt(packet, payloadBytes.Length);
                packet.Write(payloadBytes, 0, payloadBytes.Length);

                await stream.WriteAsync(packet.ToArray(), linkedCts.Token);
            }

            await stream.FlushAsync(linkedCts.Token);

            _ = await ReadVarIntAsync(stream, linkedCts.Token);
            var packetId = await ReadVarIntAsync(stream, linkedCts.Token);
            if (packetId == 0x00)
            {
                var jsonLength = await ReadVarIntAsync(stream, linkedCts.Token);
                var jsonBytes = new byte[jsonLength];
                var bytesRead = 0;
                while (bytesRead < jsonLength)
                {
                    var read = await stream.ReadAsync(jsonBytes.AsMemory(bytesRead, jsonLength - bytesRead), linkedCts.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    bytesRead += read;
                }

                var json = Encoding.UTF8.GetString(jsonBytes, 0, bytesRead);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                result.IsOnline = true;

                if (root.TryGetProperty("players", out var playersEl))
                {
                    if (playersEl.TryGetProperty("online", out var onlineEl))
                    {
                        result.OnlinePlayers = onlineEl.GetInt32();
                    }

                    if (playersEl.TryGetProperty("max", out var maxEl))
                    {
                        result.MaxPlayers = maxEl.GetInt32();
                    }
                }

                if (root.TryGetProperty("version", out var versionEl) && versionEl.TryGetProperty("name", out var verNameEl))
                {
                    result.Version = verNameEl.GetString() ?? "1.21.1";
                }

                if (root.TryGetProperty("description", out var descEl))
                {
                    if (descEl.ValueKind == JsonValueKind.String)
                    {
                        result.Motd = descEl.GetString() ?? string.Empty;
                    }
                    else if (descEl.TryGetProperty("text", out var textEl))
                    {
                        result.Motd = textEl.GetString() ?? string.Empty;
                    }
                }
            }
        }
        catch
        {
            result.IsOnline = false;
        }

        return result;
    }

    private static void WriteVarInt(Stream stream, int value)
    {
        while ((value & -128) != 0)
        {
            stream.WriteByte((byte)((value & 127) | 128));
            value = (int)((uint)value >> 7);
        }
        stream.WriteByte((byte)value);
    }

    private static async Task<int> ReadVarIntAsync(Stream stream, CancellationToken cancellationToken)
    {
        var numRead = 0;
        var result = 0;
        byte read;
        var buffer = new byte[1];
        do
        {
            var count = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            if (count == 0)
            {
                throw new EndOfStreamException();
            }

            read = buffer[0];
            var value = read & 127;
            result |= value << (7 * numRead);
            numRead++;
            if (numRead > 5)
            {
                throw new InvalidOperationException("VarInt is too big");
            }
        } while ((read & 128) != 0);

        return result;
    }

    private static void WriteString(Stream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        WriteVarInt(stream, bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteShort(Stream stream, short value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        stream.Write(bytes, 0, bytes.Length);
    }
}
