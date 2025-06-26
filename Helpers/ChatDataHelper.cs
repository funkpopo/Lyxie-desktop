using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using System.IO;
using Lyxie_desktop.Models;

namespace Lyxie_desktop.Helpers
{
    public static class ChatDataHelper
    {
        private static readonly string DatabasePath = Path.Combine(AppDataHelper.GetAppDataRootPath(), "chat_history.db");
        private static readonly string ConnectionString = $"Data Source={DatabasePath}";

        /// <summary>
        /// 初始化数据库
        /// </summary>
        public static async Task InitializeDatabaseAsync()
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            // 创建会话表
            var createSessionsTable = @"
                CREATE TABLE IF NOT EXISTS ChatSessions (
                    Id TEXT PRIMARY KEY,
                    Title TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    LastUpdatedAt TEXT NOT NULL,
                    LastMessage TEXT
                );";

            // 创建消息表
            var createMessagesTable = @"
                CREATE TABLE IF NOT EXISTS ChatMessages (
                    Id TEXT PRIMARY KEY,
                    SessionId TEXT NOT NULL,
                    Sender TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    Timestamp TEXT NOT NULL,
                    MessageType INTEGER NOT NULL,
                    FOREIGN KEY (SessionId) REFERENCES ChatSessions (Id) ON DELETE CASCADE
                );";

            using var sessionCommand = new SqliteCommand(createSessionsTable, connection);
            await sessionCommand.ExecuteNonQueryAsync();

            using var messageCommand = new SqliteCommand(createMessagesTable, connection);
            await messageCommand.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// 获取所有会话
        /// </summary>
        public static async Task<ObservableCollection<ChatSession>> GetAllSessionsAsync()
        {
            var sessions = new ObservableCollection<ChatSession>();

            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            var query = "SELECT Id, Title, CreatedAt, LastUpdatedAt, LastMessage FROM ChatSessions ORDER BY LastUpdatedAt DESC";
            using var command = new SqliteCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var session = new ChatSession
                {
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    CreatedAt = DateTime.Parse(reader.GetString(2)),
                    LastUpdatedAt = DateTime.Parse(reader.GetString(3)),
                    LastMessage = reader.IsDBNull(4) ? "" : reader.GetString(4)
                };
                sessions.Add(session);
            }

            return sessions;
        }

        /// <summary>
        /// 创建新会话
        /// </summary>
        public static async Task<ChatSession> CreateNewSessionAsync(string? title = null)
        {
            var session = new ChatSession
            {
                Id = Guid.NewGuid().ToString(),
                Title = title ?? "新对话",
                CreatedAt = DateTime.Now,
                LastUpdatedAt = DateTime.Now
            };

            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            var query = @"
                INSERT INTO ChatSessions (Id, Title, CreatedAt, LastUpdatedAt, LastMessage)
                VALUES (@Id, @Title, @CreatedAt, @LastUpdatedAt, @LastMessage)";

            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@Id", session.Id);
            command.Parameters.AddWithValue("@Title", session.Title);
            command.Parameters.AddWithValue("@CreatedAt", session.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@LastUpdatedAt", session.LastUpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@LastMessage", session.LastMessage);

            await command.ExecuteNonQueryAsync();
            return session;
        }

        /// <summary>
        /// 更新会话
        /// </summary>
        public static async Task UpdateSessionAsync(ChatSession session)
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            var query = @"
                UPDATE ChatSessions 
                SET Title = @Title, LastUpdatedAt = @LastUpdatedAt, LastMessage = @LastMessage
                WHERE Id = @Id";

            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@Id", session.Id);
            command.Parameters.AddWithValue("@Title", session.Title);
            command.Parameters.AddWithValue("@LastUpdatedAt", session.LastUpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@LastMessage", session.LastMessage);

            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// 删除会话及其所有消息
        /// </summary>
        public static async Task DeleteSessionAsync(string sessionId)
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            // 删除消息
            var deleteMessagesQuery = "DELETE FROM ChatMessages WHERE SessionId = @SessionId";
            using var deleteMessagesCommand = new SqliteCommand(deleteMessagesQuery, connection);
            deleteMessagesCommand.Parameters.AddWithValue("@SessionId", sessionId);
            await deleteMessagesCommand.ExecuteNonQueryAsync();

            // 删除会话
            var deleteSessionQuery = "DELETE FROM ChatSessions WHERE Id = @Id";
            using var deleteSessionCommand = new SqliteCommand(deleteSessionQuery, connection);
            deleteSessionCommand.Parameters.AddWithValue("@Id", sessionId);
            await deleteSessionCommand.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// 获取会话的所有消息
        /// </summary>
        public static async Task<List<ChatMessage>> GetSessionMessagesAsync(string sessionId)
        {
            var messages = new List<ChatMessage>();

            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT Id, SessionId, Sender, Content, Timestamp, MessageType 
                FROM ChatMessages 
                WHERE SessionId = @SessionId 
                ORDER BY Timestamp ASC";

            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@SessionId", sessionId);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var message = new ChatMessage
                {
                    Id = reader.GetString(0),
                    SessionId = reader.GetString(1),
                    Sender = reader.GetString(2),
                    Content = reader.GetString(3),
                    Timestamp = DateTime.Parse(reader.GetString(4)),
                    MessageType = (MessageType)reader.GetInt32(5)
                };
                messages.Add(message);
            }

            return messages;
        }

        /// <summary>
        /// 保存消息
        /// </summary>
        public static async Task SaveMessageAsync(ChatMessage message)
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            var query = @"
                INSERT INTO ChatMessages (Id, SessionId, Sender, Content, Timestamp, MessageType)
                VALUES (@Id, @SessionId, @Sender, @Content, @Timestamp, @MessageType)";

            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@Id", message.Id);
            command.Parameters.AddWithValue("@SessionId", message.SessionId);
            command.Parameters.AddWithValue("@Sender", message.Sender);
            command.Parameters.AddWithValue("@Content", message.Content);
            command.Parameters.AddWithValue("@Timestamp", message.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@MessageType", (int)message.MessageType);

            await command.ExecuteNonQueryAsync();

            // 更新会话的最后消息时间
            await UpdateSessionLastMessageAsync(message.SessionId, message.Content, message.Timestamp);
        }

        /// <summary>
        /// 更新会话的最后消息
        /// </summary>
        private static async Task UpdateSessionLastMessageAsync(string sessionId, string lastMessage, DateTime lastUpdatedAt)
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            // 截取消息预览（最多50个字符）
            var preview = lastMessage.Length > 50 ? lastMessage.Substring(0, 50) + "..." : lastMessage;

            var query = @"
                UPDATE ChatSessions 
                SET LastMessage = @LastMessage, LastUpdatedAt = @LastUpdatedAt 
                WHERE Id = @Id";

            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@Id", sessionId);
            command.Parameters.AddWithValue("@LastMessage", preview);
            command.Parameters.AddWithValue("@LastUpdatedAt", lastUpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));

            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// 搜索会话
        /// </summary>
        public static async Task<ObservableCollection<ChatSession>> SearchSessionsAsync(string keyword)
        {
            var sessions = new ObservableCollection<ChatSession>();

            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT DISTINCT s.Id, s.Title, s.CreatedAt, s.LastUpdatedAt, s.LastMessage
                FROM ChatSessions s
                LEFT JOIN ChatMessages m ON s.Id = m.SessionId
                WHERE s.Title LIKE @Keyword OR m.Content LIKE @Keyword
                ORDER BY s.LastUpdatedAt DESC";

            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@Keyword", $"%{keyword}%");
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var session = new ChatSession
                {
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    CreatedAt = DateTime.Parse(reader.GetString(2)),
                    LastUpdatedAt = DateTime.Parse(reader.GetString(3)),
                    LastMessage = reader.IsDBNull(4) ? "" : reader.GetString(4)
                };
                sessions.Add(session);
            }

            return sessions;
        }
    }
} 