using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace A_Island
{
    public sealed class ThreadSource : ObservableCollection<Thread>, IDisposable
    {
        private HttpClient client;
        private uint page_index;
        private bool has_access_token;
        private string access_token;

        public string forum_name { get; private set; }
        public uint thread_id { get; private set; }
        public bool canRefresh {get;set; }
        public bool hasLoadedMore { get; set; }
        public const string ACCESS_TOKEN = "access_token";
        public const string THREAD_URL = "http://h.acfun.tv/t/";
        public const string FORUM_URL = "http://h.acfun.tv/api/forum/get?forumName=";
        public const string FORUM_ROOT_URL = "http://h.acfun.tv/";
        public const string FORUMS_URL = "http://h.acfun.tv/api/Forums";
        public const string POST_THREAD_URL = "http://h.acfun.tv/api/thread/post_root";
        public const string POST_REPLY_URL = "http://h.acfun.tv/api/thread/post_sub";

        public ThreadSource(string forum_name = "综合版1", uint thread_id = 0)
        {
            client = new HttpClient();
            client.Timeout = new TimeSpan(0,0,30);
            page_index = 1;
            var localSettings = ApplicationData.Current.LocalSettings;
            object obj = localSettings.Values[ACCESS_TOKEN];
            if (obj == null)
            {
                access_token = null;
                has_access_token = false;
            }
            else
            {
                access_token = obj.ToString();
                has_access_token = true;
            }
            this.forum_name = forum_name;
            this.thread_id = thread_id;
            initialize();
        }

        private async void initialize()
        {
            HttpResponseMessage response = await client.GetAsync((thread_id == 0? getForumInformation():getThreadInformation()));
            response.EnsureSuccessStatusCode();
            JsonObject forum = JsonObject.Parse(await response.Content.ReadAsStringAsync());
            //if (!has_access_token && (access_token == null))
            //{
            //    access_token = forum[ACCESS_TOKEN].GetString();
            //    has_access_token = true;
            //}
        }

        private void getThreadList(JsonArray thread_list)
        {
            if (thread_list != null && thread_list.Count > 0)
            {
                for (uint i = 0; i < thread_list.Count; i++)
                {
                    Thread thread = new Thread();
                    JsonObject obj = thread_list.GetObjectAt(i);
                    thread.Content = WebUtility.HtmlDecode(obj["content"].GetString()).Replace("<br>", "\n").Replace("<br/>", "\n");
                    if (thread.Content.StartsWith("<font color="))
                    {
                        thread.Content_Head = thread.Content.Substring(thread.Content.IndexOf(">>>") + 1, thread.Content.IndexOf("</font>") - thread.Content.IndexOf(">>>") - 1);
                        thread.Content = thread.Content.Substring(thread.Content.IndexOf("</font>") + 7);
                    }
                    thread.Sage = obj["sage"].GetBoolean();
                    thread.Lock = obj["lock"].GetBoolean();
                    thread.Forum = (uint)obj["forum"].GetNumber();
                    JsonArray reply_list = obj["recentReply"].GetArray();
                    thread.RecentReply = new uint[reply_list.Count];
                    for (uint j = 0; j < reply_list.Count; j++)
                    {
                        thread.RecentReply[j] = (uint)reply_list.GetNumberAt(j);
                    }
                    thread.ReplyCount = (uint)obj["replyCount"].GetNumber();
                    thread.ID = (uint)obj["id"].GetNumber();
                    thread.UID = obj["uid"].GetString();
                    if (thread.UID.StartsWith("<font style="))
                    {
                        thread.UID_Special = thread.UID.Substring(thread.UID.IndexOf(">") + 1, thread.UID.IndexOf("</font>") - thread.UID.IndexOf(">") - 1);
                        thread.UID = null;
                    }
                    thread.Name = obj["name"].GetString();
                    thread.Email = obj["email"].Stringify().Equals("null") ? null : obj["email"].GetString();
                    thread.Title = obj["title"].GetString();
                    thread.CreatedAt = new DateTime(1970, 1, 1).AddMilliseconds(obj["createdAt"].GetNumber()).AddHours(8);
                    thread.UpdatedAt = new DateTime(1970, 1, 1).AddMilliseconds(obj["updatedAt"].GetNumber()).AddHours(8);
                    thread.Image = obj["image"].Stringify().Equals("null") ? null : "http://static.acfun.mm111.net/h" + obj["image"].GetString();
                    thread.Thumb = obj["thumb"].Stringify().Equals("null") ? null : "http://static.acfun.mm111.net/h" + obj["thumb"].GetString();
                    if (thread.Image == null && thread.Thumb == null)
                    {
                        thread.OrgImage = new BitmapImage();
                        thread.ThImage = new BitmapImage();
                    }
                    else
                    {
                        thread.ThImage = new BitmapImage(new Uri(thread.Thumb));
                    }
                    if (!this.Contains(thread))
                    {
                        this.Add(thread);
                    }
                }
            }
        }

        private void getReplyList(JsonArray reply_list)
        {
            if (reply_list != null && reply_list.Count > 0)
            {
                for (uint i = 0; i < reply_list.Count; i++)
                {
                    Thread thread = new Thread();
                    JsonObject obj = reply_list.GetObjectAt(i);
                    thread.Content = WebUtility.HtmlDecode(obj["content"].GetString()).Replace("<br>", "\n").Replace("<br/>", "\n");
                    if (thread.Content.StartsWith("<font color="))
                    {
                        thread.Content_Head = thread.Content.Substring(thread.Content.IndexOf(">>>") + 1, thread.Content.IndexOf("</font>") - thread.Content.IndexOf(">>>") - 1);
                        thread.Content = thread.Content.Substring(thread.Content.IndexOf("</font>") + 7);
                    }
                    thread.Sage = obj["sage"].GetBoolean();
                    thread.Lock = obj["lock"].GetBoolean();
                    thread.Forum = (uint)obj["forum"].GetNumber();
                    thread.ReplyCount = (uint)obj["replyCount"].GetNumber();
                    thread.ID = (uint)obj["id"].GetNumber();
                    thread.UID = obj["uid"].GetString();
                    if (thread.UID.StartsWith("<font style="))
                    {
                        thread.UID_Special = thread.UID.Substring(thread.UID.IndexOf(">") + 1, thread.UID.IndexOf("</font>") - thread.UID.IndexOf(">") - 1);
                        thread.UID = null;
                    }
                    thread.Name = obj["name"].GetString();
                    thread.Email = obj["email"].Stringify().Equals("null") ? null : obj["email"].GetString();
                    thread.Title = obj["title"].GetString();
                    thread.CreatedAt = new DateTime(1970, 1, 1).AddMilliseconds(obj["createdAt"].GetNumber()).AddHours(8);
                    thread.UpdatedAt = new DateTime(1970, 1, 1).AddMilliseconds(obj["updatedAt"].GetNumber()).AddHours(8);
                    thread.Image = obj["image"].Stringify().Equals("null") ? null : "http://static.acfun.mm111.net/h" + obj["image"].GetString();
                    thread.Thumb = obj["thumb"].Stringify().Equals("null") ? null : "http://static.acfun.mm111.net/h" + obj["thumb"].GetString();
                    if (thread.Image == null && thread.Thumb == null)
                    {
                        thread.OrgImage = new BitmapImage();
                        thread.ThImage = new BitmapImage();
                    }
                    else
                    {
                        thread.ThImage = new BitmapImage(new Uri(thread.Thumb));
                    }
                    if (!this.Contains(thread))
                    {
                        this.Add(thread);
                    }
                }
            }
        }

        private string getForumInformation()
        {
            return FORUM_ROOT_URL + forum_name + ".json?page=" + page_index + "&access_token=" + access_token;
        }

        private string getThreadInformation()
        {
            return THREAD_URL + thread_id + ".json?page=" + page_index + "&access_token=" + access_token;
        }

        public async void postThread(string content, string name = null, string email = null, string title = null, Windows.Storage.StorageFile file = null)
        {
            PostThread thread = new PostThread();
            thread.forumName = forum_name;
            thread.thread.content = content;
            thread.thread.file = (file == null ? null : await readFile(file));
            thread.thread.name = name;
            thread.thread.email = email;
            thread.thread.title = title;
            post(typeof(PostThread), thread, POST_THREAD_URL);
        }

        public async void postReply(uint parentID, string content, uint replyID, string name = null, string email = null, string title = null, Windows.Storage.StorageFile file = null)
        {
            PostReply reply = new PostReply();
            reply.parentID = parentID;
            reply.thread.content = (replyID == 0 ? content : ">>No." + replyID + "\r\n" + content);
            reply.thread.name = name;
            reply.thread.email = email;
            reply.thread.title = title;
            reply.thread.file = (file == null ? null : await readFile(file));
            post(typeof(PostReply), reply, POST_REPLY_URL);
        }

        private async Task<byte[]> readFile(Windows.Storage.StorageFile file)
        {
            IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
            var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            byte[] data = new byte[stream.Size];
            reader.ReadBytes(data);
            return data;
        }

        private async void post(Type type, Chuan obj, string url)
        {
            DataContractJsonSerializer json = new DataContractJsonSerializer(type);
            MemoryStream ms = new MemoryStream();
            json.WriteObject(ms, obj);
            ms.Position = 0;
            StreamReader sr = new StreamReader(ms);
            StringContent httpcontent = new StringContent(sr.ReadToEnd(), Encoding.UTF8, "application/json");
#if DEBUG
            Debug.WriteLine(await httpcontent.ReadAsStringAsync());
#endif
            HttpResponseMessage msg = await client.PostAsync(url+"?access_token=" + access_token, httpcontent);
            msg.EnsureSuccessStatusCode();
        }

        private async void checkAccessToken(HttpResponseMessage msg)
        {
            JsonObject json = JsonObject.Parse(await msg.Content.ReadAsStringAsync());
            if (has_access_token && !access_token.Equals(json[ACCESS_TOKEN].GetString()))
            {
                access_token = json[ACCESS_TOKEN].GetString();
            }
        }

        public async void changeForum(string forumName)
        {
            this.forum_name = forumName;
            this.page_index = 1;
            this.Clear();
            try
            {
                HttpResponseMessage response = await client.GetAsync(getForumInformation());
                response.EnsureSuccessStatusCode();
                JsonObject forum = JsonObject.Parse(await response.Content.ReadAsStringAsync());
                if (!forum.ContainsKey("success") || !forum["success"].GetBoolean())
                {
                    throw new ConnectionFailedException();
                }
                else
                {
                    getThreadList(forum["data"].GetObject().GetNamedArray("threads"));
                }
#if DEBUG
                Debug.WriteLine("changeForum()");
#endif
                checkAccessToken(response);
            }
            catch (TaskCanceledException)
            {

            }
            catch (ConnectionFailedException)
            {

            }
        }

        public async void changeThread(uint id)
        {
            this.thread_id = id;
            this.page_index = 1;
            this.Clear();
            HttpResponseMessage response = await client.GetAsync(getThreadInformation());
            response.EnsureSuccessStatusCode();
            JsonObject forum = JsonObject.Parse(await response.Content.ReadAsStringAsync());
            getReplyList(forum["replys"].GetArray());
#if DEBUG
            Debug.WriteLine("changeThread()");
#endif
            checkAccessToken(response);
        }

        public async void loadMore()
        {
            if (this.Count % 10 == 0)
            {
                page_index++;
            }
            HttpResponseMessage response = await client.GetAsync((thread_id == 0 ? getForumInformation() : getThreadInformation()));
            response.EnsureSuccessStatusCode();
            JsonObject forum = JsonObject.Parse(await response.Content.ReadAsStringAsync());
            if (thread_id == 0)
            {
                getThreadList(forum["data"].GetObject().GetNamedArray("threads"));
            }
            else
            {
                getReplyList(forum["replys"].GetArray());
            }
            
#if DEBUG
            Debug.WriteLine("loadMore()");
#endif
            checkAccessToken(response);
        }

        public void Dispose()
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values[ACCESS_TOKEN] = access_token;
            client.Dispose();
            client = null;
        }
    }

    [DataContract]
    public abstract class Chuan
    {
        [DataMember(Name = "thread")]
        public Thread thread { get; set; }

        public Chuan() { thread = new Thread(); }

        [DataContract]
        public sealed class Thread
        {
            [DataMember(Name = "name")]
            public string name { get; set; }
            [DataMember(Name = "email")]
            public string email { get; set; }
            [DataMember(Name = "title")]
            public string title { get; set; }
            [DataMember(Name = "content")]
            public string content { get; set; }
            [DataMember(Name = "file")]
            public byte[] file { get; set; }
        }
    }

    [DataContract]
    public sealed class PostReply : Chuan
    {
        [DataMember(Name = "parentID")]
        public uint parentID { get; set; }

        public PostReply():base(){}
    }

    [DataContract]
    public sealed class PostThread : Chuan
    {
        [DataMember(Name = "forumName")]
        public string forumName { get; set; }

        public PostThread():base(){}
    }

    public sealed class Thread : IEquatable<Thread>
    {
        public bool Lock { get; set; }
        public bool Sage { get; set; }
        public string UID { get; set; }
        public string UID_Special { get; set; }
        public uint ID { get; set; }
        public uint ReplyCount { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string Content_Head { get; set; }
        public string Image { get; set; }
        public string Thumb { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public BitmapImage OrgImage { get; set; }
        public BitmapImage ThImage { get; set; }
        public uint Forum { get; set; }
        public uint parent { get; set; }
        public uint[] RecentReply { get; set; }

        public Thread() { }

        public override string ToString()
        {
            return string.Format("{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}", Lock, Sage, ID, UID, ReplyCount, Name, Email, Title, Content, Image, Thumb, CreatedAt);
        }

        public bool Equals(Thread other)
        {
            return this.ID.Equals(other.ID);
        }
    }

    public sealed class ForumList : ObservableCollection<Item>, IDisposable
    {
        private const string FORUM_LIST = "ForumList";

        public ForumList()
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            object obj = localSettings.Values[FORUM_LIST];
            if (obj != null)
            {
                this.Clear();
                foreach (var x in obj as List<Item>)
                {
                    this.Add(x);
                }
            }
            else
            {
                this.Add(new Item("综合版1"));
                this.Add(new Item("欢乐恶搞"));
                this.Add(new Item("推理"));
                this.Add(new Item("技术宅"));
                this.Add(new Item("询问2"));
                this.Add(new Item("料理"));
                this.Add(new Item("貓版"));
                this.Add(new Item("音乐"));
                this.Add(new Item("体育"));
                this.Add(new Item("军武"));
                this.Add(new Item("模型"));
                this.Add(new Item("考试"));
                this.Add(new Item("WIKI"));
                this.Add(new Item("数码"));
                this.Add(new Item("动画"));
                this.Add(new Item("漫画"));
                this.Add(new Item("小说"));
                this.Add(new Item("二次创作"));
                this.Add(new Item("VOCALOID"));
                this.Add(new Item("东方Project"));
                this.Add(new Item("游戏"));
                this.Add(new Item("EVE"));
                this.Add(new Item("DNF"));
                this.Add(new Item("扩散性百万亚瑟王"));
                this.Add(new Item("信喵之野望"));
                this.Add(new Item("LOL"));
                this.Add(new Item("DOTA"));
                this.Add(new Item("Minecraft"));
                this.Add(new Item("MUG"));
                this.Add(new Item("MUGEN"));
                this.Add(new Item("WOT"));
                this.Add(new Item("WOW"));
                this.Add(new Item("卡牌桌游"));
                this.Add(new Item("怪物猎人"));
                this.Add(new Item("索尼"));
                this.Add(new Item("任天堂"));
                this.Add(new Item("口袋妖怪"));
                this.Add(new Item("AC大逃杀"));
                this.Add(new Item("KKK德州扑克"));
                this.Add(new Item("一骑当先"));
                this.Add(new Item("AC页游王国印记"));
                this.Add(new Item("AC页游王者召唤"));
                this.Add(new Item("AC页游街机三国"));
                this.Add(new Item("AC页游热血海贼王"));
                this.Add(new Item("AKB"));
                this.Add(new Item("cosplay"));
                this.Add(new Item("影视"));
                this.Add(new Item("摄影"));
                this.Add(new Item("声优"));
                this.Add(new Item("值班室"));
            }
        }
        
        private int indexOf(string Name)
        {
            for (int i = 0; i < this.Count; i++ )
            {
                if (this[i].Name.Equals(Name))
                {
                    return i;
                }
            }
            return -1;
        }
        
        public void onClick(string Name)
        {
            int index = indexOf(Name);
            if(index >= 0)
                this[index].Priority++;
        }
        
        public void Dispose()
        {
            this.OrderBy(item => item.Priority);
            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values[FORUM_LIST] = (this as ObservableCollection<Item>);
        }
    }

    public sealed class Item
    {
        public string Name { get; set; }
        public int Priority { get; set; }

        public Item(string Name = "") { this.Name = Name; this.Priority = 0; }
    }

    public sealed class ConnectionFailedException : Exception { }
}
