using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.IO;
using System.Text.RegularExpressions;
using Jukebox.Server.Models;
using System.Collections.ObjectModel;
using System.Web;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Diagnostics;
using Jint;

namespace Jukebox.Server.DataProviders
{
    class VKComDataProvider_new : IDataProvider
    {
        static Cookie _cookie = null;

        public TrackSource GetSourceType()
        {
            return TrackSource.VK;
        }

        public IList<Track> Search(string query)
        {
            var result = new List<Track>();
            try
            {
                if (_cookie == null)
                {
                    var res = Auth(Config.GetInstance().VKLogin, Config.GetInstance().VKPassword, out _cookie);
                    if (!res)
                    {
                        throw new Exception("Failed to authorize at vk.com.");
                    }
                }

                string url = "https://m.vk.com/search";
                string parameters = "al=1&c[q]=" + HttpUtility.UrlEncode(query).ToUpper() + "&c[section]=audio";

                string content = MakeRequest(url, parameters, _cookie);

                string techInfo = @"(<!--.*<!>|<!--.*->|<!>)";
                content = Regex.Replace(content, techInfo, String.Empty);

                // Нет метки о том, что ничего не найдено
                if (content != "")
                {
                    var doc = new HtmlAgilityPack.HtmlDocument();
                    doc.Load((new StringReader(content)));
                    content = null;
                    var elements = doc.DocumentNode.SelectNodes("//*[@class='ai_body']");

                    foreach (var divTrack in elements)
                    {
                        try
                        {
                            var track = new Track();

                            var nodeWithUrl = divTrack.SelectSingleNode(".//input");
                            if (nodeWithUrl == null)
                            {
                                continue;
                            }

                            track.Uri = new Uri(divTrack.SelectSingleNode(".//input").GetAttributeValue("value", ""), UriKind.Absolute);
                            track.Title = PrepareDataLine(divTrack.SelectSingleNode(".//*[@class='ai_title']").InnerText);
                            track.Singer = PrepareDataLine(divTrack.SelectSingleNode(".//*[@class='ai_artist']").InnerText);
                            var durationNode = divTrack.SelectSingleNode(".//*[@class='ai_dur']");
                            track.Duration = TimeSpan.FromSeconds(Convert.ToDouble(durationNode.GetAttributeValue("data-dur", "0")));
                            track.Source = TrackSource.VK;

                            track.Id = track.GetHash();

                            result.Add(track);
                        }
                        catch (Exception innerE)
                        {
                            Debug.WriteLine("VKComDataProvider (track parse): " + innerE.Message);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine("VKComDataProvider (global): " + e.Message);
                Debug.WriteLine("Query: " + query);
            }



            return new ReadOnlyCollection<Track>(result);
        }

        string PrepareDataLine(string content)
        {
            content = content.Replace("<em class=\"found\">", "");
            content = content.Replace("</em>", "");
            content = content.Trim();
            return content;
        }

        string MakeRequest(string url, string parameters, Cookie cookie, string method = "POST")
        {
            Stream responseStream = MakeRequestStream(url, parameters, cookie, method);
            string content = "";

            using (var reader = new StreamReader(responseStream, Encoding.UTF8))
            {
                content = reader.ReadToEnd();
            }

            return content;
        }

        Stream MakeRequestStream(string url, string parameters, Cookie cookie, string method = "POST")
        {
            HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
            request.CookieContainer = new CookieContainer();
            request.CookieContainer.Add(cookie);
            request.ContentType = @"application/x-www-form-urlencoded";
            request.Method = method;

            byte[] message = Encoding.UTF8.GetBytes(parameters);
            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(message, 0, message.Length);
                requestStream.Close();
            }

            return request.GetResponse().GetResponseStream();
        }

        public byte[] Download(Track track)
        {
            try
            {
                var trackUrl = track.Uri.OriginalString;
                trackUrl = Regex.Unescape(trackUrl).Replace("\"", "");
                Jint.Engine engine = new Engine()
                    .Execute(getRevealScript());
                trackUrl = engine.Invoke("s", trackUrl).ToString();
                track.Uri = new Uri(trackUrl);

                WebDownload downloader = new WebDownload();
                downloader.Timeout = Config.GetInstance().DownloadTimeout;

                Stream trackStream = downloader.OpenRead(track.Uri);
                MemoryStream resultStream = new MemoryStream();

                byte[] buffer = new byte[4096];
                while (trackStream.CanRead)
                {
                    Array.Clear(buffer, 0, buffer.Length);
                    int bytesRead = trackStream.Read(buffer, 0, buffer.Length);

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    resultStream.Write(buffer, 0, bytesRead);
                }

                return resultStream.ToArray();
            }
            catch (Exception e)
            {
                Console.WriteLine("VKComDataProvider error: " + e.Message);
            }
            return null;
        }

        private bool Auth(String email, String pass, out Cookie cookie)
        {
            HttpWebRequest landingRequest = (HttpWebRequest)System.Net.WebRequest.Create("https://vk.com/");
            landingRequest.Timeout = 100000;
            landingRequest.AllowAutoRedirect = false;
            landingRequest.UserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/51.0.2704.103 Safari/537.36";
            var landingResponse = landingRequest.GetResponse();
            var landingResponseString = new StreamReader(landingResponse.GetResponseStream()).ReadToEnd();
            var ipHRegexMatch = Regex.Match(landingResponseString, "<input type=\"hidden\" name=\"ip_h\" value=\"(.+)\"");
            var lgHRegexMatch = Regex.Match(landingResponseString, "<input type=\"hidden\" name=\"lg_h\" value=\"(.+)\"");

            var ipH = ipHRegexMatch.Result("$1");
            var lgH = lgHRegexMatch.Result("$1");

            string landingResponseHeaders = landingResponse.Headers.ToString();

            Regex remixlhkRegex = new Regex("remixlhk=([a-z0-9]+); exp");
            var remixlhk = remixlhkRegex.Match(landingResponseHeaders).NextMatch().Groups[1].Value;

            HttpWebRequest wrPOSTURL = (HttpWebRequest)System.Net.WebRequest.Create(
                "http://login.vk.com/?act=login" //&email=" + email + "&pass=" + pass + "&lg_h=" + lgH
            );
            wrPOSTURL.UserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/51.0.2704.103 Safari/537.36";
            wrPOSTURL.AllowAutoRedirect = false;
            wrPOSTURL.Timeout = 100000;
            wrPOSTURL.Method = "POST";
            wrPOSTURL.Referer = "https://vk.com/";
            wrPOSTURL.Host = "login.vk.com";
            wrPOSTURL.Headers.Add("Accept-Language: ru,en-US;q=0.7,en;q=0.3");
            wrPOSTURL.Headers.Add("DNT: 1");
            wrPOSTURL.Headers.Add("Accept-Encoding: gzip, deflate");
            wrPOSTURL.ServicePoint.Expect100Continue = false;

            wrPOSTURL.Accept = "text/html, application/xhtml+xml, */*";
            wrPOSTURL.ContentType = "application/x-www-form-urlencoded";
            wrPOSTURL.CookieContainer = new CookieContainer();
            wrPOSTURL.CookieContainer.Add(new Cookie("remixlang", "0", "/", ".vk.com"));
            wrPOSTURL.CookieContainer.Add(new Cookie("remixrefkey", "b99ef30c41aa2e48fc", "/", ".vk.com"));
            wrPOSTURL.CookieContainer.Add(new Cookie("_ym_uid", "1463544547390306689", "/", ".vk.com"));
            wrPOSTURL.CookieContainer.Add(new Cookie("remixstid", "1592289695_fc0ec279667da8de94", "/", ".vk.com"));
            wrPOSTURL.CookieContainer.Add(new Cookie("_ym_isad", "2", "/", ".vk.com"));
            wrPOSTURL.CookieContainer.Add(new Cookie("remixflash", "22.0.0", "/", ".vk.com"));
            wrPOSTURL.CookieContainer.Add(new Cookie("remixscreen_depth", "24", "/", ".vk.com"));
            wrPOSTURL.CookieContainer.Add(new Cookie("remixdt", "14400", "/", ".vk.com"));
            wrPOSTURL.CookieContainer.Add(new Cookie("remixtst", "457fbc51", "/", ".vk.com"));
            wrPOSTURL.CookieContainer.Add(new Cookie("remixseenads", "0", "/", ".vk.com"));
            wrPOSTURL.CookieContainer.Add(new Cookie("t", "1f6850662e05f3b274c1a56f", "/", ".vk.com"));
            wrPOSTURL.CookieContainer.Add(new Cookie("remixlhk", remixlhk, "/", ".vk.com"));

            using (var writer = new StreamWriter(wrPOSTURL.GetRequestStream()))
            {
                writer.Write("act=login&");
                writer.Write("role=al_frame&");
                writer.Write("ip_h=" + ipH + "&");
                writer.Write("lg_h=" + lgH + "&");
                writer.Write("email=" + email + "&");
                writer.Write("pass=" + pass + "&");
                writer.Write("expire=&");
                writer.Write("captcha_sid=&");
                writer.Write("captcha_key=&");
                writer.Write("_origin=https%3A%2F%2Fvk.com");
            }

            string location = wrPOSTURL.GetResponse().Headers["Location"];

            HttpWebRequest redirectRequest = (HttpWebRequest)System.Net.WebRequest.Create(location);
            redirectRequest.AllowAutoRedirect = false;
            redirectRequest.Timeout = 100000;
            string redirectHeaders = redirectRequest.GetResponse().Headers.ToString();

            Regex sidregex = new Regex("remixsid=([a-z0-9]+); exp");
            Match ssid = sidregex.Match(redirectHeaders);
            string sid = ssid.Groups[1].Value;
            cookie = new Cookie("remixsid", sid, "/", ".vk.com");

            if (String.IsNullOrEmpty(sid))
            {
                return false;
            }
            return true;
        }

        private String getRevealScript()
        {
            return @"
var id = " + Config.GetInstance().VKId + @"; //Ваш userid
var n = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMN0PQRSTUVWXYZO123456789+/=',
    i = {
      v: function (e) {
        return e.split('').reverse().join('')
      },
      r: function (e, t) {
        e = e.split('');
        for (var i, o = n + n, s = e.length; s--;) {
          i = o.indexOf(e[s]), ~i && (e[s] = o.substr(i - t, 1));
        }
        return e.join('')
      },
      s: function (e, t) {
        var n = e.length;
        if (n) {
          var i = r(e, t),
              o = 0;
          for (e = e.split(''); ++o < n;) {
            e[o] = e.splice(i[n - 1 - o], 1, e[o])[0];
          }
          e = e.join('')
        }
        return e
      },
      i: function (e, t) {
        return i.s(e, t ^ id)
      },
      x: function (e, t) {
        var n = [];
        return t = t.charCodeAt(0), each(e.split(''), function (e, i) {
          n.push(String.fromCharCode(i.charCodeAt(0) ^ t))
        }), n.join('')
      }
    };

function o() {
  return false;
}

function s(e) {
  if (!o() && ~e.indexOf('audio_api_unavailable')) {
    var t = e.split('?extra=')[1].split('#'),
        n = '' === t[1] ? '' : a(t[1]);
    if (t = a(t[0]), 'string' != typeof n || !t) {
      return e;
    }
    n = n ? n.split(String.fromCharCode(9)) : [];
    for (var s, r, l = n.length; l--;) {
      if (r = n[l].split(String.fromCharCode(11)), s = r.splice(0, 1, t)[0], !i[s]) {
        return e;
      }
      t = i[s].apply(null, r)
    }
    if (t && 'http' === t.substr(0, 4)) {
      return t
    }
  }
  return e
}

function a(e) {
  if (!e || e.length % 4 == 1) {
    return !1;
  }
  for (var t, i, o = 0, s = 0, a = ''; i = e.charAt(s++);) {
    i = n.indexOf(i), ~i && (t = o % 4 ? 64 * t + i : i, o++ % 4)
    && (a += String.fromCharCode(255 & t >> (-2 * o & 6)));
  }
  return a
}

function r(e, t) {
  var n = e.length,
      i = [];
  if (n) {
    var o = n;
    for (t = Math.abs(t); o--;) {
      t = (n * (o + 1) ^ t + o) % n, i[o] = t
    }
  }
  return i
}
";
        }

    }
}
