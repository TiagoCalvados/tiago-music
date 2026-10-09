using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Speech.Synthesis;

namespace TiagoMusic {
 public static class Tests {
  static List<string> log=new List<string>();
  static void Check(bool valid,string message) { if(!valid) throw new Exception(message); log.Add("PASS: "+message); }
  public static int Run(string root) {
   try {
    Directory.CreateDirectory(root); string library=Path.Combine(root,"Copied music"); Directory.CreateDirectory(library);
    string a=Path.Combine(library,"Artist A","Album"), b=Path.Combine(library,"Artist B"); Directory.CreateDirectory(a); Directory.CreateDirectory(b);
    string first=Path.Combine(a,"First song.mp3"), cafe=Path.Combine(b,"Café.mp3"), duplicateA=Path.Combine(a,"Same.mp3"), duplicateB=Path.Combine(b,"Same.mp3");
    foreach(string f in new [] { first,cafe,duplicateA,duplicateB }) File.WriteAllText(f,"Test placeholder, not audio");
    string playlist=Path.Combine(library,"Phone favourites.m3u8");
    File.WriteAllText(playlist,"#EXTM3U\n#EXTINF:10,My first song\n/storage/emulated/0/Music/Artist A/Album/First song.mp3\n/storage/1234-5678/Music/Artist B/Café.mp3\nSame.mp3\n/storage/emulated/0/Music/Artist A/Album/Same.mp3\nMissing.mp3\nhttps://example.invalid/radio.mp3\n",new UTF8Encoding(true));
    File.WriteAllText(Path.Combine(library,"Relative.m3u"),"#EXTM3U\nArtist A/Album/First song.mp3\nArtist B/Café.mp3\n",new UTF8Encoding(false));
    File.WriteAllBytes(Path.Combine(library,"Legacy.m3u"),Encoding.GetEncoding(1252).GetBytes("Artist B/Café.mp3\n"));
    string before=Convert.ToBase64String(File.ReadAllBytes(playlist));
    var r=Importer.Scan(library); Check(r.SongCount==4 && r.Playlists.Count==3,"Recursive playlist and song discovery");
    var p=r.Playlists.Single(x=>x.Name=="Phone favourites");
    Check(p.Tracks.Count==3 && p.Tracks[0].Path==first && p.Tracks[1].Path==cafe && p.Tracks[2].Path==duplicateA,"Android internal/SD paths and longest suffix matching");
    Check(p.Tracks[0].Title=="My first song","EXTINF display titles preserved");
    Check(p.Unmatched.Count==3 && p.Unmatched.Contains("Same.mp3"),"Ambiguous filename, missing file and internet URL left unresolved");
    Check(r.Playlists.Single(x=>x.Name=="Relative").Tracks.Count==2,"Relative paths and UTF-8 without BOM");
    Check(r.Playlists.Single(x=>x.Name=="Legacy").Tracks.Count==1,"Legacy Windows playlist encoding");
    Check(Convert.ToBase64String(File.ReadAllBytes(playlist))==before && File.ReadAllText(first)=="Test placeholder, not audio","Original playlist and songs unchanged");
    string bare=Path.Combine(root,"Only songs"); Directory.CreateDirectory(bare); File.WriteAllText(Path.Combine(bare,"One.mp3"),"placeholder");
    var folder=Importer.Scan(bare); Check(folder.Playlists.Count==1 && folder.Playlists[0].Source==bare && folder.Playlists[0].Tracks.Count==1,"Songs-only folder offered as playlist");
    string empty=Path.Combine(root,"Empty"); Directory.CreateDirectory(empty); Check(Importer.Scan(empty).Playlists.Count==0,"Empty folder handled");
    var order=new PlaybackOrder(new Random(42));
    for(int n=1;n<=30;n++) { order.Reset(n,true,-1); for(int cycle=0;cycle<8;cycle++) { var values=Enumerable.Range(0,n).Select(x=>order.Next()).ToList(); Check(values.Distinct().Count()==n,"Shuffle visits every song once, size "+n+", cycle "+cycle); } }
    order.Reset(5,true,-1); int x1=order.Next(), x2=order.Next(),x3=order.Next(); Check(order.Previous()==x2 && order.Previous()==x1 && order.Next()==x2 && order.Next()==x3,"Previous and forward history stay consistent");
    order.Reset(4,false,-1); Check(order.Next()==0 && order.Next()==1 && order.Next()==2 && order.Next()==3 && order.Next()==0,"Sequential playback loops in order");
    order.Reset(3,true,1); Check(order.Current==1 && new [] { order.Next(),order.Next() }.Distinct().Count()==2 && order.Current!=1,"Manual start excluded from remainder of first shuffle");
    order.Reset(0,true,-1); Check(order.Next()==-1 && order.Previous()==-1,"Empty playback queue is safe");
    string state=Path.Combine(root,"Test settings"); var s=new Settings { Shuffle=false, Greeting=true, Volume=0.35, ActiveId=p.Id, CurrentPath=first, Position=12.5 }; s.Playlists.Add(p); Storage.Save(state,s);
    var saved=Storage.Load(state); Check(!saved.Shuffle && saved.Greeting && saved.Volume==0.35 && saved.Position==12.5 && saved.ActiveId==p.Id && saved.Playlists[0].Unmatched.Count==3,"Settings, playlist entries and playback position persist");
    var startup=Storage.LoadForPlayback(state); Check(startup.Shuffle && startup.ActiveId==p.Id && startup.Volume==0.35 && startup.Greeting && startup.Playlists[0].Tracks.Count==p.Tracks.Count,"Reopening enables shuffle after it was turned off, while retaining playlist and other settings");
    Storage.Save(state,s); File.WriteAllText(Path.Combine(state,"settings.xml"),"corrupted"); var recovered=Storage.Load(state); Check(recovered.Playlists.Count==1 && Storage.Warning.Contains("backup"),"Corrupt settings recover from backup");
    string clean=Path.Combine(root,"Fresh settings"); var fresh=Storage.Load(clean); Check(fresh.Shuffle && !fresh.Greeting,"Shuffle defaults on and greeting is optional");
    using(var speech=new SpeechSynthesizer()) { Check(speech.GetInstalledVoices().Any(x=>x.Enabled),"Offline Windows speech voice available"); speech.SetOutputToWaveFile(Path.Combine(root,"Greeting test.wav")); speech.Speak("Tiago, which playlist would you like to listen to?"); }
    Check(new FileInfo(Path.Combine(root,"Greeting test.wav")).Length>1000,"Exact voice greeting synthesized offline");
    var lyrics=LyricDocument.Parse("[ar:Test]\n[offset:500]\n[00:01.00]First line\n[00:02.250][00:04.25]Repeated line\n[00:05] \n");
    Check(lyrics.Timed && lyrics.Lines.Count==4 && lyrics.Lines[0].Seconds==1.5,"LRC timestamps, offset and repeated-line timestamps parse");
    Check(lyrics.At(1.49)==-1 && lyrics.At(1.5)==0 && lyrics.At(3)==1 && lyrics.At(5)==2,"Highlight selects the current timed line");
    Check(lyrics.At(5.6)==3 && lyrics.At(1.6)==0,"Lyric highlighting follows backwards seeking and instrumental gaps");
    var plain=LyricDocument.Parse("Just a plain line\nAnother line"); Check(!plain.Timed && plain.At(10)==-1 && plain.Lines.Count==2,"Plain lyrics display without false synchronized highlighting");
    var enhanced=LyricDocument.Parse("[00:02.123]<00:02.123>Test <00:02.600>words\n[00:99]bad timestamp"); Check(enhanced.Lines.Count==1 && enhanced.Lines[0].Text=="Test words","Enhanced LRC word markers removed and invalid seconds ignored");
    LyricsStore.Save(root,first,"[00:00]Cached line\n[00:01]Second cached line",true); var cached=LyricsStore.Load(root,first); Check(cached.Timed && cached.Lines.Count==2,"Timed lyrics cached separately from music files for offline use");
    LyricsStore.Save(root,first,"Plain replacement",false); Check(!LyricsStore.Load(root,first).Timed,"Plain lyric replacement clears stale cached timing");
    Check(File.ReadAllText(first)=="Test placeholder, not audio","Saving lyrics leaves original audio intact");
    var identity=SongIdentity.FromName("Gorillaz - Baby Queen (Official Lyric Video)");
    Check(identity.Artist=="Gorillaz" && identity.Title=="Baby Queen","Automatic search removes video labels from filenames");
    var wrongArtist=new LyricsRecord { artistName="Different artist",trackName="Baby Queen",duration=219,syncedLyrics="[00:00]Wrong artist" };
    var wrongVersion=new LyricsRecord { artistName="Gorillaz",trackName="Baby Queen",duration=360,syncedLyrics="[00:00]Different recording" };
    var rightPlain=new LyricsRecord { artistName="Gorillaz",trackName="Baby Queen",duration=219,plainLyrics="Plain test" };
    var rightTimed=new LyricsRecord { artistName="Gorillaz",trackName="Baby Queen",duration=221,syncedLyrics="[00:00]Timed test" };
    Check(identity.Match(new [] {wrongArtist,wrongVersion,rightPlain,rightTimed},219)==rightTimed,"Automatic matching chooses timed lyrics for matching artist, title and duration");
    Check(identity.Match(new [] {wrongArtist,wrongVersion},219)==null,"Automatic lookup rejects wrong artists and incompatible recordings");
    Check(SongIdentity.FromName("Gorillaz Baby Queen Official Lyric Video").Match(new [] {rightTimed},219)==rightTimed,"Automatic matching supports filenames without an artist/title dash");
    var cancelled=new System.Threading.CancellationTokenSource(); cancelled.Cancel(); bool stopped=false;
    try { LyricsStore.Search("Do not send this test",219,cancelled.Token); } catch(OperationCanceledException) { stopped=true; } finally { cancelled.Dispose(); }
    Check(stopped,"Cancelled song lookup stops before making a network request");
    Check(new Settings().AutomaticLyrics && Storage.Load(state).AutomaticLyrics,"Automatic lyrics default on and migrate into older saved settings");
    string audio=Path.Combine(root,"Playback test.mp3");
    if(File.Exists(audio)) {
     File.WriteAllText(Path.ChangeExtension(audio,".lrc"),"[00:00.00]Get ready on bass\n[00:01.00]First test line\n[00:02.00]Second test line\n[00:03.00]Third test line\n[00:04.00]Keep the groove\n[00:05.00]Fifth test line\n[00:06.00]Last test line\n[00:07.00]Ending\n");
     var demo=new Playlist { Id="lyrics-test", Name="Lyrics test", Source=root, Root=root }; demo.Tracks.Add(new Track { Path=audio, Title="Timed lyrics test" });
     var demoState=new Settings { ActiveId=demo.Id, CurrentPath=audio, Volume=0.05 }; demoState.Playlists.Add(demo); Storage.Save(Path.Combine(root,"Lyrics UI"),demoState);
    }
    File.WriteAllLines(Path.Combine(root,"self-test-results.txt"),log.Concat(new [] { "ALL TESTS PASSED" })); return 0;
   } catch(Exception e) { log.Add("FAIL: "+e); Directory.CreateDirectory(root); File.WriteAllLines(Path.Combine(root,"self-test-results.txt"),log); return 1; }
  }
  public static int AutoLyrics(string song,double duration,string root) {
   var lines=new List<string>();
   try {
    Directory.CreateDirectory(root); var identity=SongIdentity.FromName(Path.GetFileNameWithoutExtension(song));
    List<LyricsRecord> records;
    using(var cancellation=new System.Threading.CancellationTokenSource()) records=System.Threading.Tasks.Task.Factory.StartNew(()=>LyricsStore.Search(identity.Query,duration,cancellation.Token)).Result;
    var chosen=identity.Match(records,duration);
    if(chosen==null) throw new Exception("No matching record found for this test song.");
    if(chosen.instrumental) throw new Exception("Test song was listed as instrumental.");
    bool timed=!String.IsNullOrWhiteSpace(chosen.syncedLyrics); LyricsStore.Save(root,song,timed ? chosen.syncedLyrics : chosen.plainLyrics,timed);
    var saved=LyricsStore.Load(root,song); if(saved==null || saved.Lines.Count==0) throw new Exception("Fetched lyrics were not cached.");
    lines.Add("PASS: live automatic lookup matched "+chosen.artistName+" / "+chosen.trackName);
    lines.Add("PASS: cached lyrics reopen offline; timed="+saved.Timed+", lines="+saved.Lines.Count);
    if(saved.Timed && saved.At(saved.Lines[0].Seconds)<0) throw new Exception("Fetched LRC failed its timing check.");
    lines.Add("PASS: fetched lyrics parse and highlight correctly"); File.WriteAllLines(Path.Combine(root,"automatic-lyrics-results.txt"),lines); return 0;
   } catch(Exception e) { lines.Add("FAIL: "+e); Directory.CreateDirectory(root); File.WriteAllLines(Path.Combine(root,"automatic-lyrics-results.txt"),lines); return 1; }
  }
  public static int Audio(string audio,string report) {
   var lines=new List<string>(); int code=1; var app=new Application(); var m=new MediaPlayer(); var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(100) }; DateTime start=DateTime.UtcNow; int step=0; double pausePosition=0; bool ended=false;
   Action finish=delegate { timer.Stop(); m.Close(); File.WriteAllLines(report,lines); app.Shutdown(code); };
   m.MediaFailed+=delegate(object sender,ExceptionEventArgs e) { lines.Add("FAIL: "+e.ErrorException); finish(); };
   m.MediaOpened+=delegate {
    if(!m.NaturalDuration.HasTimeSpan || m.NaturalDuration.TimeSpan.TotalSeconds<4) { lines.Add("FAIL: expected test audio of at least 4 seconds"); finish(); return; }
    lines.Add("PASS: decoded MP3, duration "+m.NaturalDuration.TimeSpan); m.Volume=0.2; m.Play(); start=DateTime.UtcNow; step=1;
   };
   m.MediaEnded+=delegate { ended=true; };
   timer.Tick+=delegate {
    try {
     double t=(DateTime.UtcNow-start).TotalSeconds;
     if(step==1 && t>1) { if(m.Position.TotalSeconds<0.3) throw new Exception("playback did not advance"); lines.Add("PASS: playback advances"); m.Pause(); pausePosition=m.Position.TotalSeconds; start=DateTime.UtcNow; step=2; }
     else if(step==2 && t>0.6) { if(Math.Abs(m.Position.TotalSeconds-pausePosition)>0.2) throw new Exception("pause did not hold position"); lines.Add("PASS: pause holds position"); m.Position=TimeSpan.FromSeconds(3); start=DateTime.UtcNow; step=3; }
     else if(step==3 && t>0.3) { if(Math.Abs(m.Position.TotalSeconds-3)>0.3) throw new Exception("seek did not move"); lines.Add("PASS: seeking while paused"); m.Volume=0.65; if(Math.Abs(m.Volume-0.65)>0.01) throw new Exception("volume failed"); lines.Add("PASS: volume setting"); m.Position=TimeSpan.FromSeconds(m.NaturalDuration.TimeSpan.TotalSeconds-1); m.Play(); start=DateTime.UtcNow; step=4; }
     else if(step==4 && ended) { lines.Add("PASS: resume and MediaEnded notification"); code=0; finish(); }
     else if(t>15) throw new Exception("audio test timed out, step "+step);
    } catch(Exception e) { lines.Add("FAIL: "+e.Message); finish(); }
   };
   timer.Start(); m.Open(new Uri(Path.GetFullPath(audio))); m.Play(); app.Run(); return code;
  }
 }
}
