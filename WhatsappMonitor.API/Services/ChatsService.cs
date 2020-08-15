using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Threading.Tasks.Sources;
using WhatsappMonitor.API.Context;
using WhatsappMonitor.Shared.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Globalization;
using System.Text;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhatsappMonitor.API.Services
{
    public interface IChatsService
    {
        Task<List<Chat>> GetAllChatsEntity(int id);
        Task<Tuple<PaginationDTO, List<Chat>>> GetAllChatsPagination(int id, int pagination, int take);
        Task<int> SearchEntityChatTextByDate(int id, string date);
        Task<List<Chat>> SearchEntityChatText(string text, int id, int pagination, int take);
        Task<List<ParticipantDTO>> GetChatParticipants(int id);
        Task UpdateNameChat(int entityId, ParticipantDTO participant);
        Task<List<ParticipantDTO>> UpdateParticipantsChat(int entityId, List<ParticipantDTO> participants);
        Task DeleteNameChat(int entityId, string name);
        Task<List<ChatUploadDTO>> GetChatUploadDate(int id);
        Task<List<Upload>> GetUploadAwaiting(int id);
        Task DeleteDateChat(int entityId, ChatUploadDTO dto);
        Task<TotalFolderInfoDTO> GetFullChatInfo(int entityId, string from, string until);
        Task<List<MessagesTime>> GetChatInfoMessageCounter(int entityId, string from, string until);
        Task<List<WordsTime>> GetChatInfoWordCounter(int entityId, string from, string until);
        Task<List<UsersTime>> GetChatInfoUserCounter(int entityId, string from, string until);
        Task ProcessEntityFiles();
        Task<List<ChatPersonInfoDTO>> GetChatParticipantsInfo(int entityId, string from, string until);

    }

    public class ChatsService : IChatsService
    {
        private readonly MyDbContext _context;
        public ChatsService(MyDbContext context)
        {
            _context = context;
        }

        private async Task<bool> MessageAlreadyExist(DateTime messageTime, string message, int entityId)
        {
            var result = await _context.Chats.FirstOrDefaultAsync(c => c.Message == message && c.MessageTime == messageTime && c.EntityId == entityId);
            if (result == null)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        public async Task<List<Chat>> GetAllChatsEntity(int id)
        {
            return await _context.Chats.Where(c => c.EntityId == id).OrderBy(c => c.EntityId).ToListAsync();
        }

        public async Task<Tuple<PaginationDTO, List<Chat>>> GetAllChatsPagination(int id, int skip, int take)
        {
            var cleanTake = 100;
            var cleanSkip = 0;
            if (skip >= 0) cleanSkip = skip;
            if (take >= 0 && take <= 100) cleanTake = take;

            var chat = await _context.Chats.Where(c => c.EntityId == id).OrderByDescending(c => c.MessageTime).ToListAsync();

            var result = chat.Skip(cleanSkip).Take(cleanTake).ToList();

            var allowNext = false; //allow older messages
            var allowBack = false; //allow newer messages

            if (cleanSkip > 0) allowBack = true;
            if ((cleanSkip + cleanTake) < chat.Count()) allowNext = true;

            var pagesCounter = chat.Count() / cleanTake; //totalmessages divided by current take
            var currentPage = (cleanSkip + cleanTake) / cleanTake;

            var paginationDto = new PaginationDTO(cleanSkip, cleanTake, allowNext, allowBack, pagesCounter, currentPage);

            return new Tuple<PaginationDTO, List<Chat>>(paginationDto, result);
        }

        public async Task<int> SearchEntityChatTextByDate(int id, string date)
        {
            var parsedDate = DateTime.Parse(date);
            var chat = await _context.Chats.Where(c => c.EntityId == id && c.MessageTime >= parsedDate).OrderByDescending(c => c.MessageTime).CountAsync();

            return chat - 1;
        }

        private List<Chat> SearchChatText(string text, List<Chat> messages)
        {
            var listChat = new List<Chat>();
            foreach (var item in messages)
            {
                var lowText = text.ToLower();
                var lowerMessage = item.Message.ToLower();
                if (lowerMessage.Contains(lowText))
                {
                    listChat.Add(item);
                }
            }
            return listChat;
        }
        public async Task<List<Chat>> SearchEntityChatText(string text, int id, int pagination, int take)
        {
            var cleanTake = 25;
            var cleanPagination = 0;
            if (take >= 0 && take <= 100) cleanTake = take;
            if (pagination >= 0) cleanPagination = pagination;

            var messages = await _context.Chats.Where(c => c.EntityId == id).OrderByDescending(c => c.MessageTime).ToListAsync();
            var findText = SearchChatText(text, messages);
            var result = findText.Skip(cleanPagination).ToList(); //SearchEntityChatText
            return result;
        }

        public async Task<List<ParticipantDTO>> GetChatParticipants(int id)
        {
            var participants = new List<ParticipantDTO>();
            var users = await _context.Chats.Where(c => c.EntityId == id).Select(c => c.PersonName).Distinct().ToListAsync();
            var totalMessages = 0;
            var totalWords = 0;

            foreach (var user in users)
            {
                var firstMessage = await _context.Chats.Where(c => c.EntityId == id && c.PersonName == user).MinAsync(c => c.MessageTime);
                var lastMessage = await _context.Chats.Where(c => c.EntityId == id && c.PersonName == user).MaxAsync(c => c.MessageTime);
                var messages = await _context.Chats.Where(c => c.EntityId == id && c.PersonName == user).Select(c => c.Message).ToListAsync();
                var messageCounter = messages.Count();
                var wordCounter = 0;

                foreach (var item in messages)
                {
                    wordCounter = wordCounter + item.Split(new char[] { '.', '?', '!', ' ', ';', ':', ',' }, StringSplitOptions.RemoveEmptyEntries).Count();
                }

                totalMessages = totalMessages + messageCounter;
                totalWords = totalWords + wordCounter;

                participants.Add(new ParticipantDTO
                {
                    MessageCounter = messageCounter,
                    FirstMessage = firstMessage,
                    LastMessage = lastMessage,
                    PersonName = user,
                    NewName = user,
                    WordsCounter = wordCounter
                });
            }

            foreach (var item in participants)
            {
                item.MessageCounterPercentage = (item.MessageCounter * 100) / totalMessages;
                item.WordsCounterPercentage = (item.WordsCounter * 100) / totalWords;
            }

            return participants.OrderBy(c => c.PersonName).ToList();
        }
        public async Task UpdateNameChat(int entityId, ParticipantDTO participant)
        {
            var newName = participant.NewName;
            var oldName = participant.PersonName;

            var toUpdate = await _context.Chats.Where(c => c.PersonName == oldName && c.EntityId == entityId).ToListAsync();

            foreach (var item in toUpdate)
            {
                item.PersonName = newName;
            }
            await _context.SaveChangesAsync();

        }
        public async Task DeleteNameChat(int entityId, string name)
        {
            var toDelete = await _context.Chats.Where(c => c.EntityId == entityId && c.PersonName == name).ToListAsync();
            _context.Chats.RemoveRange(toDelete);

            await _context.SaveChangesAsync();
        }

        public async Task<List<ChatUploadDTO>> GetChatUploadDate(int id)
        {
            var chatList = new List<ChatUploadDTO>();

            var chatDates = await _context.Chats.Where(c => c.EntityId == id).Select(c => c.SystemTime).Distinct().ToListAsync();

            foreach (var date in chatDates)
            {
                var dateCounter = await _context.Chats.Where(c => c.SystemTime == date && c.EntityId == id).CountAsync();
                chatList.Add(new ChatUploadDTO
                {
                    ChatCount = dateCounter,
                    UploadDate = date
                });
            }

            return chatList;
        }

        public async Task<List<Upload>> GetUploadAwaiting(int id)
        {
            var uploadList = await _context.Uploads.Where(e => e.EntityId == id).ToListAsync();

            foreach (var item in uploadList)
            {
                item.FileContent = null;
            }

            return uploadList;
        }

        public async Task DeleteDateChat(int entityId, ChatUploadDTO dto)
        {
            var date = dto.UploadDate;
            var toDelete = await _context.Chats.Where(c => c.EntityId == entityId && c.SystemTime == date).ToListAsync();
            _context.Chats.RemoveRange(toDelete);

            await _context.SaveChangesAsync();
        }

        private async Task<ChatInfoDate> CheckDates(string from, string until)
        {
            if (String.IsNullOrWhiteSpace(from) && String.IsNullOrWhiteSpace(until))
            {
                return new ChatInfoDate { From = DateTime.Parse(from), Until = DateTime.Parse(until) };
            }
            else
            {
                var fromMin = await _context.Chats.MinAsync(c => c.MessageTime);
                var untilMax = await _context.Chats.MaxAsync(c => c.MessageTime);
                var startToFinish = new ChatInfoDate { From = fromMin, Until = untilMax };
                return startToFinish;
            }
        }

        public async Task<TotalFolderInfoDTO> GetFullChatInfo(int entityId, string from, string until)
        {
            var checkedDates = await CheckDates(from, until);
            var messages = await _context.Chats.Where(e => e.EntityId == entityId && e.MessageTime > checkedDates.From && e.MessageTime < checkedDates.Until).ToListAsync();
            var totalMessages = messages.Count();
            var wordCounter = 0;
            var superWordList = new List<string>();

            foreach (var item in messages)
            {
                var splits = item.Message.Split(new char[] { '.', '?', '!', ' ', ';', ':', ',' }, StringSplitOptions.RemoveEmptyEntries);
                wordCounter = wordCounter + splits.Count();
                foreach (var word in splits)
                {
                    if (word.Length > 5)
                    {
                        superWordList.Add(word);
                    }
                }
            }

            var commonHours = messages.GroupBy(c => c.MessageTime.Hour).Select(c => new Tuple<string, int>(c.Key.ToString(), c.Count())).ToList();

            var totalHours = commonHours.Sum(c => c.Item2);
            var commonHoursPercentage = new List<Tuple<string, double>>();

            foreach (var item in commonHours)
            {
                double percentage = (item.Item2 * 100) / totalMessages;
                commonHoursPercentage.Add(new Tuple<string, double>(item.Item1, percentage));
            }

            var commonWords = superWordList.GroupBy(c => c).Select(c => new Tuple<string, double>(c.Key.ToString(), c.Count())).OrderByDescending(c => c.Item2).Take(10).ToList();

            var total = new TotalFolderInfoDTO
            {
                TotalMessage = totalMessages,
                TotalWords = wordCounter,
                CommonWords = commonWords,
                CommonHours = commonHoursPercentage.OrderByDescending(c => c.Item2).Take(10).ToList()
            };
            return total;
        }


        public async Task<List<MessagesTime>> GetChatInfoMessageCounter(int entityId, string from, string until)
        {
            var checkedDates = await CheckDates(from, until);
            var messages = await _context.Chats.Where(e => e.EntityId == entityId && e.MessageTime > checkedDates.From && e.MessageTime < checkedDates.Until).ToListAsync();
            var totalMessages = messages.Count();
            var commonHours = messages.GroupBy(c => c.MessageTime.Hour).Select(c => new Tuple<int, int>(c.Key, c.Count())).ToList();

            var totalHours = commonHours.Sum(c => c.Item2);
            var hoursPercentage = new List<MessagesTime>();

            foreach (var item in commonHours)
            {
                double percentage = (item.Item2 * 100) / totalMessages;
                hoursPercentage.Add(new MessagesTime { Hour = item.Item1, MessagePercentage = percentage });
            }

            return hoursPercentage;
        }

        public async Task<List<UsersTime>> GetChatInfoUserCounter(int entityId, string from, string until)
        {
            var checkedDates = await CheckDates(from, until);
            var messages = await _context.Chats.Where(e => e.EntityId == entityId && e.MessageTime > checkedDates.From && e.MessageTime < checkedDates.Until).ToListAsync();
            var totalMessages = messages.Count();
            var userGrouped = messages.GroupBy(c => c.PersonName).Select(c => new Tuple<string, int>(c.Key, c.Count())).ToList();

            var usersPercentage = new List<UsersTime>();

            foreach (var item in userGrouped)
            {
                double percentage = Math.Round((double)(item.Item2 * 100) / totalMessages);
                usersPercentage.Add(new UsersTime { UserName = item.Item1, MessagePercentage = percentage });
            }

            var top10 = usersPercentage.OrderByDescending(c => c.MessagePercentage).Take(10).ToList();

            if (usersPercentage.Count > 9)
            {
                double othersPercentage = 0;
                foreach (var item in usersPercentage.Skip(10))
                {
                    othersPercentage = othersPercentage + item.MessagePercentage;
                }
                top10.Add(new UsersTime { UserName = "Others", MessagePercentage = othersPercentage });
            }

            return top10;
        }


        public async Task<List<WordsTime>> GetChatInfoWordCounter(int entityId, string from, string until)
        {
            var checkedDates = await CheckDates(from, until);
            var messages = await _context.Chats.Where(e => e.EntityId == entityId && e.MessageTime > checkedDates.From && e.MessageTime < checkedDates.Until).ToListAsync();
            var commonHours = messages
            .GroupBy(c => c.MessageTime.Hour)
            .Select(c => new Tuple<int, List<string>>(c.Key, c.Select(a => a.Message).ToList()))
            .ToList();

            var tempTuple = new List<Tuple<int, int>>();

            foreach (var item in commonHours)
            {
                var wordCounter = 0;

                foreach (var strings in item.Item2)
                {
                    var splits = strings.Split(new char[] { '.', '?', '!', ' ', ';', ':', ',' }, StringSplitOptions.RemoveEmptyEntries).Count();
                    wordCounter = wordCounter + splits;
                }

                tempTuple.Add(new Tuple<int, int>(item.Item1, wordCounter));
            }

            var totalWords = tempTuple.Sum(c => c.Item2);

            var wordsPercentage = new List<WordsTime>();

            foreach (var item in tempTuple)
            {
                double percentage = (item.Item2 * 100) / totalWords;
                wordsPercentage.Add(new WordsTime { Hour = item.Item1, MessagePercentage = percentage });
            }

            return wordsPercentage;
        }

        public async Task<List<ParticipantDTO>> UpdateParticipantsChat(int entityId, List<ParticipantDTO> participants)
        {
            //check for differentes and deletions, deletions have priority over updates
            var updateList = new List<ParticipantDTO>();
            var deleteList = new List<ParticipantDTO>();

            foreach (var item in participants)
            {
                if (item.ToDelete == false && item.PersonName != item.NewName && !String.IsNullOrWhiteSpace(item.NewName))
                {
                    await UpdateNameChat(entityId, item);
                }
                else if (item.ToDelete == true)
                {
                    await DeleteNameChat(entityId, item.PersonName);
                }
            }

            var updatedInfo = await GetChatParticipants(entityId);

            return updatedInfo;
        }

        public async Task<List<ChatPersonInfoDTO>> GetChatParticipantsInfo(int entityId, string from, string until)
        {
            var personList = new List<ChatPersonInfoDTO>();

            var checkedDates = await CheckDates(from, until);
            var Messages = await _context.Chats.Where(e => e.EntityId == entityId).ToListAsync();

            var personMessages = Messages.GroupBy(c => c.PersonName);


            foreach (var person in personMessages)
            {
                var messageCounter = person.Select(c => c.Message).Count();
                var wordCounter = 0;
                var personWordList = new List<String>();

                foreach (var message in person.Select(c => c.Message))
                {
                    var splits = message.Split(new char[] { '.', '?', '!', ' ', ';', ':', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    wordCounter = wordCounter + splits.Count();
                    foreach (var word in splits)
                    {
                        personWordList.Add(word);
                    }
                }

                var commonWords = personWordList.GroupBy(c => c).Select(c => new Tuple<string, int>(c.Key.ToString(), c.Count())).OrderByDescending(c => c.Item2).Take(10).ToList();

                var commonHours = person.GroupBy(c => c.MessageTime.Hour).Select(c => new Tuple<string, int>(c.Key.ToString(), c.Count())).ToList();
                var totalHours = commonHours.Sum(c => c.Item2);

                var commonHoursPercentage = new List<Tuple<string, double>>();

                foreach (var item in commonHours)
                {
                    double percentage = (item.Item2 * 100) / messageCounter;
                    commonHoursPercentage.Add(new Tuple<string, double>(item.Item1, percentage));
                }

                personList.Add(new ChatPersonInfoDTO
                {
                    PersonName = person.Select(c => c.PersonName).FirstOrDefault(),
                    MessageCounter = messageCounter,
                    WordsCounter = wordCounter,
                    CommonWords = commonWords,
                    Hours = commonHours
                });
            }

            var totalMessages = personList.Sum(c => c.MessageCounter);
            var totalWords = personList.Sum(c => c.WordsCounter);

            foreach (var item in personList)
            {
                var message = (item.MessageCounter * 100) / totalMessages;
                var words = (item.WordsCounter * 100) / totalWords;
                item.MessagePercentage = message;
                item.WordsPercentage = words;
            }
            return personList;
        }

        private DateTime? ValidDate(string line)
        {
            var start = 0;
            var datePosition = line.IndexOf('-', start);
            if (datePosition != -1)
            {
                var temp = line.Substring(start, datePosition - start + 1).Trim();

                if (temp.Length < 6) return null;

                var dateString = temp.Remove(temp.Length - 2);
                var parsedDate = new DateTime();
                if (DateTime.TryParseExact(dateString, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                {
                    return parsedDate;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        private String ValidSender(string line)
        {
            var start = 0;
            var datePosition = line.IndexOf('-', start);
            var valueAfterDate = line.Substring(datePosition - start + 1).Trim();
            var namePosition = valueAfterDate.IndexOf(':', start);
            if (namePosition != -1)
            {
                var temp = valueAfterDate.Substring(start, namePosition - start + 1).Trim();
                return temp.Remove(temp.Length - 1);
            }
            else
            {
                return "";
            }
        }

        private String CleanMessage(string line)
        {
            var start = 0;
            var datePosition = line.IndexOf('-', start);
            var valueAfterDate = line.Substring(datePosition - start + 1).Trim();
            var namePosition = valueAfterDate.IndexOf(':', start);
            var valueAfterName = valueAfterDate.Substring(namePosition - start + 1).Trim();

            if (!((valueAfterName.StartsWith("<")) && (valueAfterName.EndsWith(">"))))
            {
                return valueAfterName;
            }
            else
            {
                return "";
            }
        }

        private static SemaphoreSlim semaphore;

        private async Task ProcessTxt(Upload file)
        {

            var systemTime = DateTime.Now;
            var chatList = new List<Chat>();
            var toString = Encoding.UTF8.GetString(file.FileContent);
            var entityChat = await _context.Chats.Where(c => c.EntityId == file.EntityId).Select(c => new Tuple<string, DateTime>(c.Message, c.MessageTime)).ToListAsync();
            var hashSet = new HashSet<Tuple<string, DateTime>>(entityChat);

            string[] lines = toString.Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None
            );

            //not a fan of this approach
            var linesCounter = lines.Count() - 1;

            var messageDate = ValidDate(lines[0]);
            var messageSender = ValidSender(lines[0]);
            var messageText = CleanMessage(lines[0]);

            for (int i = 0; i < linesCounter; i++)
            {
                var date = ValidDate(lines[i]);
                var sender = ValidSender(lines[i]);
                var message = CleanMessage(lines[i]);

                if (date != null)
                {
                    if (String.IsNullOrWhiteSpace(message) == false)
                    {
                        if (String.IsNullOrWhiteSpace(messageText) == false && String.IsNullOrWhiteSpace(messageSender) == false)
                        {

                            if (!(hashSet.Contains(new Tuple<string, DateTime>(messageText, messageDate.Value))))
                            {

                                var newChat = new Chat(messageSender, messageDate.Value, systemTime, messageText, file.EntityId);
                                chatList.Add(newChat);

                                if (chatList.Count > 9999)
                                {
                                    _context.Chats.AddRange(chatList);
                                    await _context.SaveChangesAsync();
                                    chatList.Clear();
                                }
                            }
                        }

                        messageDate = date;
                        messageSender = sender;
                        messageText = message;
                    }
                }
                else
                {
                    //Keep adding the message text until a new line is avaliable.
                    messageText = String.Concat(messageText, " \n ", message);
                }
            }
            _context.Chats.AddRange(chatList);
            await _context.SaveChangesAsync();

            _context.Uploads.Remove(file);
            await _context.SaveChangesAsync();
        }
        private async Task ProcessJson(Upload file)
        {
            var systemTime = DateTime.Now;
            var toString = Encoding.UTF8.GetString(file.FileContent);
            var chatList = new List<Chat>();
            var jsonChatList = JsonSerializer.Deserialize<List<RootWhatsapp>>(toString);
            var entityChat = await _context.Chats.Where(c => c.EntityId == file.EntityId).Select(c => new Tuple<string, DateTime>(c.Message, c.MessageTime)).ToListAsync();
            var hashSet = new HashSet<Tuple<string, DateTime>>(entityChat);

            //!await MessageAlreadyExist(tempDate, chat.MsgContent, file.EntityId)
            foreach (var chat in jsonChatList)
            {
                var tempDate = DateTime.Parse(chat.Date);
                if (!(hashSet.Contains(new Tuple<string, DateTime>(chat.MsgContent, tempDate))))
                {
                    var newChat = new Chat(chat.From, tempDate, systemTime, chat.MsgContent, file.EntityId);
                    chatList.Add(newChat);

                    if (chatList.Count > 5000)
                    {
                        _context.Chats.AddRange(chatList);
                        await _context.SaveChangesAsync();
                        chatList.Clear();
                    }
                }
            }

            _context.Chats.AddRange(chatList);
            await _context.SaveChangesAsync();

            _context.Uploads.Remove(file);
            await _context.SaveChangesAsync();
        }
        public async Task ProcessEntityFiles()
        {
            semaphore = new SemaphoreSlim(1, 1);

            await semaphore.WaitAsync();

            try
            {
                var fileList = await _context.Uploads.ToListAsync();

                foreach (var file in fileList)
                {
                    file.InProcess = "Yes";
                    await _context.SaveChangesAsync();

                    if (file.FileName.EndsWith("txt"))
                    {
                        await ProcessTxt(file);
                    }
                    else if (file.FileName.EndsWith("json"))
                    {
                        await ProcessJson(file);
                    }
                    else
                    {
                        _context.Uploads.Remove(file);
                        await _context.SaveChangesAsync();
                    }
                }
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
