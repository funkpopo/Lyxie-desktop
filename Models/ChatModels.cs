using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Lyxie_desktop.Models
{
    /// <summary>
    /// 聊天会话模型
    /// </summary>
    public class ChatSession : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString();
        private string _title = "新对话";
        private DateTime _createdAt = DateTime.Now;
        private DateTime _lastUpdatedAt = DateTime.Now;
        private string _lastMessage = "";

        public string Id
        {
            get => _id;
            set
            {
                _id = value;
                OnPropertyChanged();
            }
        }

        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                OnPropertyChanged();
            }
        }

        public DateTime CreatedAt
        {
            get => _createdAt;
            set
            {
                _createdAt = value;
                OnPropertyChanged();
            }
        }

        public DateTime LastUpdatedAt
        {
            get => _lastUpdatedAt;
            set
            {
                _lastUpdatedAt = value;
                OnPropertyChanged();
            }
        }

        public string LastMessage
        {
            get => _lastMessage;
            set
            {
                _lastMessage = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 聊天消息模型
    /// </summary>
    public class ChatMessage : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString();
        private string _sessionId = "";
        private string _sender = "";
        private string _content = "";
        private DateTime _timestamp = DateTime.Now;
        private MessageType _messageType = MessageType.User;

        public string Id
        {
            get => _id;
            set
            {
                _id = value;
                OnPropertyChanged();
            }
        }

        public string SessionId
        {
            get => _sessionId;
            set
            {
                _sessionId = value;
                OnPropertyChanged();
            }
        }

        public string Sender
        {
            get => _sender;
            set
            {
                _sender = value;
                OnPropertyChanged();
            }
        }

        public string Content
        {
            get => _content;
            set
            {
                _content = value;
                OnPropertyChanged();
            }
        }

        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                _timestamp = value;
                OnPropertyChanged();
            }
        }

        public MessageType MessageType
        {
            get => _messageType;
            set
            {
                _messageType = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 聊天历史管理器
    /// </summary>
    public class ChatHistory : INotifyPropertyChanged
    {
        private ObservableCollection<ChatSession> _sessions = new();
        private ChatSession? _currentSession;

        public ObservableCollection<ChatSession> Sessions
        {
            get => _sessions;
            set
            {
                _sessions = value;
                OnPropertyChanged();
            }
        }

        public ChatSession? CurrentSession
        {
            get => _currentSession;
            set
            {
                _currentSession = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 消息类型枚举
    /// </summary>
    public enum MessageType
    {
        User = 0,
        Assistant = 1,
        System = 2,
        Error = 3
    }
} 