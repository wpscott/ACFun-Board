using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml.Media.Imaging;

namespace A_Island
{
    class AcFunViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<Thread> _source;
        private string forum_name;
        private uint thread_id;
        private bool refresh, load;

        public bool canRefresh { get { return refresh; } set { refresh = value; } }
        public bool hasLoadedMore { get { return load; } set { load = value; } }

        public ObservableCollection<Thread> Source
        {
            get
            {
                return _source;
            }
            set
            {
                _source = value;
                NotifyPropertyChanged("Source");
            }
        }

        private HttpClient client;
        private uint page_index;
        private bool has_access_token;
        private string access_token;

        public const string ACCESS_TOKEN = "access_token";
        public const string FORUM_ROOT_URL = "http://h.acfun.tv/";
        public const string THREAD_URL = "http://h.acfun.tv/t/";
        public const uint THREAD_PER_PAGE = 10;

        public event PropertyChangedEventHandler PropertyChanged;
        public string ForumName { get { return forum_name; } private set { forum_name = value; NotifyPropertyChanged("ForumName"); } }
        public uint ThreadID { get { return thread_id; } private set { thread_id = value; NotifyPropertyChanged("ThreadID"); } }

        public AcFunViewModel(string forum_name = "综合版1", uint thread_id = 0)
        {
            _source = new ObservableCollection<Thread>();
            client = new HttpClient();
            client.Timeout = new TimeSpan(0, 0, 30);
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
            this.ForumName = forum_name;
            this.ThreadID = thread_id;
            initialize();
        }

        public async void changeForum(string forumName)
        {
            this.forum_name = forumName;
            this.page_index = 1;
            Source.Clear();
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
            Source.Clear();
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
            if (Source.Count % THREAD_PER_PAGE == 0)
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

        private async void checkAccessToken(HttpResponseMessage msg)
        {
            JsonObject json = JsonObject.Parse(await msg.Content.ReadAsStringAsync());
            if (has_access_token && !access_token.Equals(json[ACCESS_TOKEN].GetString()))
            {
                access_token = json[ACCESS_TOKEN].GetString();
            }
        }

        private async void initialize()
        {
            HttpResponseMessage response = await client.GetAsync((thread_id == 0 ? getForumInformation() : getThreadInformation()));
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
                    if (!Source.Contains(thread))
                    {
                        Source.Add(thread);
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
                    if (!Source.Contains(thread))
                    {
                        Source.Add(thread);
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

        private void NotifyPropertyChanged(string propName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
            }
        }
    }
}
