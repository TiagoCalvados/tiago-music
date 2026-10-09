using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Threading;

namespace TiagoMusic {
 public class LyricLine {
  public double Seconds { get; set; }
  public string Text { get; set; }
  public override string ToString() { return String.IsNullOrWhiteSpace(Text) ? "♪" : Text; }
 }
 public class LyricDocument {
  public List<LyricLine> Lines = new List<LyricLine>();
  public bool Timed;
  public string Source;
  public int At(double seconds) {
   if(!Timed) return -1;
   int lo=0, hi=Lines.Count-1, found=-1;
   while(lo<=hi) { int mid=(lo+hi)/2; if(Lines[mid].Seconds<=seconds) { found=mid; lo=mid+1; } else hi=mid-1; }
   return found;
  }
  public static LyricDocument Parse(string content) {
   var document=new LyricDocument(); var timestamp=new Regex(@"\[(\d{1,3}):(\d{2})(?:[\.:](\d{1,3}))?\]"); double offset=0;
   Match offsetMatch=Regex.Match(content,@"\[offset:([+-]?\d+)\]",RegexOptions.IgnoreCase);
   if(offsetMatch.Success) { double value; if(Double.TryParse(offsetMatch.Groups[1].Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out value)) offset=value/1000.0; }
   foreach(string raw in content.TrimStart('\uFEFF').Split(new [] {'\r','\n'},StringSplitOptions.RemoveEmptyEntries)) {
    MatchCollection stamps=timestamp.Matches(raw); if(stamps.Count==0) continue;
    string text=timestamp.Replace(raw,"").Trim();
    // Enhanced LRC word tags are shown as ordinary line text; line timings drive highlighting.
    text=Regex.Replace(text,@"<\d{1,3}:\d{2}(?:\.\d+)?>","");
    foreach(Match stamp in stamps) {
     int minutes=Int32.Parse(stamp.Groups[1].Value), seconds=Int32.Parse(stamp.Groups[2].Value);
     if(seconds>=60) continue;
     double fraction=stamp.Groups[3].Success ? Double.Parse("0."+stamp.Groups[3].Value,CultureInfo.InvariantCulture) : 0;
     document.Lines.Add(new LyricLine { Seconds=Math.Max(0,minutes*60+seconds+fraction+offset), Text=text });
    }
   }
   document.Timed=document.Lines.Count>0;
   if(document.Timed) document.Lines=document.Lines.OrderBy(x=>x.Seconds).ToList();
   else foreach(string raw in content.Split('\n')) { string line=raw.TrimEnd('\r'); if(!Regex.IsMatch(line,@"^\[(ar|ti|al|by|offset|length|re|ve):",RegexOptions.IgnoreCase)) document.Lines.Add(new LyricLine { Text=line }); }
   return document;
  }
 }
 public class LyricsRecord {
  public long id { get; set; }
  public string trackName { get; set; }
  public string artistName { get; set; }
  public string albumName { get; set; }
  public double duration { get; set; }
  public bool instrumental { get; set; }
  public string plainLyrics { get; set; }
  public string syncedLyrics { get; set; }
  public override string ToString() { return artistName+" — "+trackName+"\n"+albumName+" · "+TimeSpan.FromSeconds(Math.Max(0,duration)).ToString(@"m\:ss")+" · "+(!String.IsNullOrWhiteSpace(syncedLyrics) ? "Timed lyrics" : instrumental ? "Instrumental" : "Plain lyrics"); }
 }
 public static class LyricsStore {
  static DateTime blockedUntil=DateTime.MinValue;
  static readonly object gate=new object();
  static string Key(string track) { using(var hash=SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(track).ToUpperInvariant()))).Replace("-",""); }
  static string CacheFile(string directory,string track,string ext) { return Path.Combine(directory,"Lyrics",Key(track)+ext); }
  public static LyricDocument Load(string directory,string track) {
   foreach(string file in new [] { CacheFile(directory,track,".lrc"), Path.ChangeExtension(track,".lrc"), CacheFile(directory,track,".txt"), Path.ChangeExtension(track,".txt") }) {
    if(!File.Exists(file)) continue;
    if(new FileInfo(file).Length>1024*1024) throw new IOException("Lyric file is too large (maximum 1 MB).");
    string content; byte[] bytes=File.ReadAllBytes(file); try { content=new UTF8Encoding(false,true).GetString(bytes); } catch(DecoderFallbackException) { content=Encoding.Default.GetString(bytes); }
    var document=LyricDocument.Parse(content); document.Source=file.StartsWith(Path.Combine(directory,"Lyrics"),StringComparison.OrdinalIgnoreCase) ? "Saved lyrics · offline" : "Local lyric file · offline"; return document;
   }
   return null;
  }
  public static void Save(string directory,string track,string content,bool timed) {
   if(content.Length>1024*1024) throw new IOException("Lyric file is too large (maximum 1 MB).");
   Directory.CreateDirectory(Path.Combine(directory,"Lyrics")); string file=CacheFile(directory,track,timed ? ".lrc" : ".txt");
   File.WriteAllText(file,content,new UTF8Encoding(false));
   // A manually selected plain version replaces an older cached timed version.
   if(!timed && File.Exists(CacheFile(directory,track,".lrc"))) File.Delete(CacheFile(directory,track,".lrc"));
  }
  public static List<LyricsRecord> Search(string query,double currentDuration) { return Search(query,currentDuration,CancellationToken.None); }
  public static List<LyricsRecord> Search(string query,double currentDuration,CancellationToken cancellation) {
   if(String.IsNullOrWhiteSpace(query)) return new List<LyricsRecord>();
   lock(gate) {
    cancellation.ThrowIfCancellationRequested();
    if(DateTime.UtcNow<blockedUntil) {
     double remaining=(blockedUntil-DateTime.UtcNow).TotalMilliseconds;
     if(remaining>550) throw new IOException("The lyrics service asked us to wait. Try again in "+Math.Ceiling(remaining/1000)+" seconds.");
     if(cancellation.WaitHandle.WaitOne((int)Math.Max(1,remaining))) cancellation.ThrowIfCancellationRequested();
    }
    cancellation.ThrowIfCancellationRequested();
    // Automatic lookups request only the current song, plus explicit manual searches.
    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
    var request=(HttpWebRequest)WebRequest.Create("https://lrclib.net/api/search?q="+Uri.EscapeDataString(query.Trim()));
    request.UserAgent="TiagoMusic/1.0.0 (+https://github.com/TiagoCalvados/tiago-music)"; request.Timeout=15000; request.ReadWriteTimeout=15000; request.Accept="application/json";
    using(var registration=cancellation.Register(delegate { request.Abort(); })) {
    try {
     using(var response=(HttpWebResponse)request.GetResponse()) {
      if(!response.ContentType.StartsWith("application/json",StringComparison.OrdinalIgnoreCase)) throw new IOException("Lyrics service returned an unexpected response.");
      using(var stream=response.GetResponseStream()) using(var buffer=new MemoryStream()) {
       byte[] chunk=new byte[8192]; int count;
       while((count=stream.Read(chunk,0,chunk.Length))>0) { cancellation.ThrowIfCancellationRequested(); if(buffer.Length+count>5*1024*1024) throw new IOException("Lyrics service response was too large."); buffer.Write(chunk,0,count); }
       var serializer=new JavaScriptSerializer { MaxJsonLength=5*1024*1024 };
       var records=serializer.Deserialize<List<LyricsRecord>>(Encoding.UTF8.GetString(buffer.ToArray())) ?? new List<LyricsRecord>();
       return records.Where(x=>!String.IsNullOrWhiteSpace(x.syncedLyrics) || !String.IsNullOrWhiteSpace(x.plainLyrics) || x.instrumental).OrderBy(x=>String.IsNullOrWhiteSpace(x.syncedLyrics) ? 1 : 0).ThenBy(x=>Math.Abs(x.duration-currentDuration)).Take(30).ToList();
      }
     }
    } catch(WebException e) {
     cancellation.ThrowIfCancellationRequested();
     using(var response=e.Response as HttpWebResponse) {
      if(response!=null && ((int)response.StatusCode==429 || (int)response.StatusCode==503)) {
       int delay; DateTime deadline; string retry=response.Headers["Retry-After"];
       if(Int32.TryParse(retry,out delay)) blockedUntil=DateTime.UtcNow.AddSeconds(Math.Max(1,delay)); else if(DateTime.TryParse(retry,CultureInfo.InvariantCulture,DateTimeStyles.AdjustToUniversal,out deadline)) blockedUntil=deadline.ToUniversalTime(); else blockedUntil=DateTime.UtcNow.AddSeconds(60);
       throw new IOException("Lyrics service is busy. Please try later.");
      }
     }
     throw new IOException("Could not reach the lyrics service. Music and saved lyrics still work offline.",e);
    } finally { if(blockedUntil<DateTime.UtcNow) blockedUntil=DateTime.UtcNow.AddMilliseconds(500); }
    }
   }
  }
 }
 public class SongIdentity {
  public string Artist;
  public string Title;
  public string Query { get { return (Artist+" "+Title).Trim(); } }
  public static string Clean(string value) {
   value=(value ?? "").Replace('_',' ');
   value=Regex.Replace(value,@"[\(\[][^\)\]]*(official|video|visuali[sz]er|lyric|\d+\s*kbps|hq|hd)[^\)\]]*[\)\]]","",RegexOptions.IgnoreCase);
   value=Regex.Replace(value,@"\b(?:official\s+)?(?:music\s+video|lyric\s+video|official\s+audio|official\s+video|official\s+visuali[sz]er)\b","",RegexOptions.IgnoreCase);
   value=Regex.Replace(value,@"\s*[\(\[]\s*(?:feat\.?|ft\.?)\s+.*?[\)\]]","",RegexOptions.IgnoreCase);
   return Regex.Replace(value,@"\s+"," ").Trim();
  }
  static string Normal(string value) { return Regex.Replace(Clean(value).ToLowerInvariant(),@"[^\p{L}\p{N}]",""); }
  static bool ArtistMatches(string a,string b) {
   if(Normal(a)==Normal(b)) return true;
   var first=Regex.Matches(a.ToLowerInvariant(),@"[\p{L}\p{N}]+").Cast<Match>().Select(x=>x.Value).ToList();
   var second=Regex.Matches(b.ToLowerInvariant(),@"[\p{L}\p{N}]+").Cast<Match>().Select(x=>x.Value).ToList();
   return first.Count>0 && second.Count>0 && (first.All(x=>second.Contains(x)) || second.All(x=>first.Contains(x)));
  }
  public static SongIdentity FromName(string name) {
   string value=Clean(name); string[] pieces=Regex.Split(value,@"\s+[-–—]\s+",RegexOptions.None);
   return pieces.Length>=2 ? new SongIdentity { Artist=pieces[0].Trim(), Title=String.Join(" - ",pieces.Skip(1)).Trim() } : new SongIdentity { Artist="", Title=value };
  }
  public LyricsRecord Match(IEnumerable<LyricsRecord> records,double duration) {
   var matching=records.Where(x=>x!=null && (x.instrumental || !String.IsNullOrWhiteSpace(x.syncedLyrics) || !String.IsNullOrWhiteSpace(x.plainLyrics)))
    .Where(x=>String.IsNullOrEmpty(Artist) ? Normal(Title)==Normal(x.artistName+" "+x.trackName) : Normal(Title)==Normal(x.trackName) && ArtistMatches(Artist,x.artistName))
    .Where(x=>duration<=0 || Math.Abs(x.duration-duration)<=15).ToList();
   return matching.OrderBy(x=>String.IsNullOrWhiteSpace(x.syncedLyrics) ? 1 : 0).ThenBy(x=>Math.Abs(x.duration-duration)).FirstOrDefault();
  }
 }
}
