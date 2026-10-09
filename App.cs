using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Threading;
using System.Speech.Synthesis;
using Forms = System.Windows.Forms;

namespace TiagoMusic {
 public class Track {
  public string Path { get; set; }
  public string Title { get; set; }
  [XmlIgnore] public string Folder { get { return System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Path)); } }
 }
 public class Playlist {
  public string Id { get; set; }
  public string Name { get; set; }
  public string Source { get; set; }
  public string Root { get; set; }
  public List<Track> Tracks { get; set; }
  public List<string> Unmatched { get; set; }
  public Playlist() { Tracks = new List<Track>(); Unmatched = new List<string>(); }
  public override string ToString() { return Name + "  ·  " + Tracks.Count + " songs" + (Unmatched.Count > 0 ? "  ·  " + Unmatched.Count + " unmatched" : ""); }
 }
 public class Settings {
  public bool Shuffle { get; set; }
  public bool Greeting { get; set; }
  public double Volume { get; set; }
  public string ActiveId { get; set; }
  public string CurrentPath { get; set; }
  public double Position { get; set; }
  public bool ShowLyrics { get; set; }
  public double LyricsOffset { get; set; }
  public bool AutomaticLyrics { get; set; }
  public List<Playlist> Playlists { get; set; }
  public Settings() { Shuffle = true; Volume = 0.7; ShowLyrics = true; AutomaticLyrics = true; Playlists = new List<Playlist>(); }
 }
 public static class Storage {
  public static string Warning;
  public static Settings Load(string directory) {
   Warning = null;
   string path = System.IO.Path.Combine(directory, "settings.xml");
   foreach (string candidate in new [] { path, path + ".bak" }) {
    if (!File.Exists(candidate)) continue;
    try { using (var f = File.OpenRead(candidate)) {
     var s = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(f);
     s.Volume = Double.IsNaN(s.Volume) ? 0.7 : Math.Max(0, Math.Min(1, s.Volume));
     if (s.Playlists == null) s.Playlists = new List<Playlist>();
     if (candidate.EndsWith(".bak")) Warning = "Recovered saved settings from the backup.";
     return s;
    }} catch (Exception) { Warning = "Saved settings could not be read. You can add your music folder again; your music is intact."; }
   }
   return new Settings();
  }
  public static Settings LoadForPlayback(string directory) {
   var settings=Load(directory);
   // Each new listening session starts in shuffle, even if it was turned off last time.
   settings.Shuffle=true;
   return settings;
  }
  public static void Save(string directory, Settings s) {
   Directory.CreateDirectory(directory);
   string path = System.IO.Path.Combine(directory, "settings.xml"), temp = path + ".tmp";
   using (var f = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { new XmlSerializer(typeof(Settings)).Serialize(f, s); f.Flush(true); }
   if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
  }
 }
 public class ImportResult {
  public List<Playlist> Playlists = new List<Playlist>();
  public List<string> Warnings = new List<string>();
  public int SongCount;
 }
 public static class Importer {
  static readonly HashSet<string> Extensions = new HashSet<string>(new [] { ".mp3", ".wav", ".wma", ".m4a" }, StringComparer.OrdinalIgnoreCase);
  static string Normalize(string path) { return path.Replace('\\','/').Trim('/'); }
  static IEnumerable<string> Walk(string root, List<string> warnings) {
   var pending = new Stack<string>(); pending.Push(root);
   while (pending.Count > 0) {
    string dir = pending.Pop(); string[] files, dirs;
    try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); }
    catch (Exception e) { if (!(e is IOException || e is UnauthorizedAccessException)) throw; warnings.Add("Could not read folder: " + dir); continue; }
    foreach (string f in files) yield return f;
    foreach (string d in dirs) {
     try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0) pending.Push(d); }
     catch (Exception e) { if (!(e is IOException || e is UnauthorizedAccessException)) throw; warnings.Add("Could not read folder: " + d); }
    }
   }
  }
  static string Resolve(string entry, string playlistFile, string root, HashSet<string> songs, Dictionary<string,List<string>> names) {
   string raw = entry.Trim().Trim('"');
   Uri uri;
   if (Uri.TryCreate(raw, UriKind.Absolute, out uri) && !uri.IsFile && raw.Contains("://")) return null;
   if (uri != null && uri.IsFile) raw = uri.LocalPath;
   string normalized = Normalize(raw);
   string windowsPath = raw.Replace('/', '\\');
   var candidates = new List<string>();
   try { candidates.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(playlistFile), windowsPath))); } catch (Exception) {}
   try { candidates.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(root, windowsPath.TrimStart('\\')))); } catch (Exception) {}
   foreach (string c in candidates) if (songs.Contains(c)) return c;
   // Match longest shared path suffix first; never choose arbitrarily between duplicate filenames.
   string name = normalized.Split('/').Last(); List<string> matches;
   if (!names.TryGetValue(name, out matches)) {
    try { normalized = Uri.UnescapeDataString(normalized); name = normalized.Split('/').Last(); } catch (Exception) {}
    if (!names.TryGetValue(name, out matches)) return null;
   }
   if (matches.Count == 1) return matches[0];
   string[] parts = normalized.Split('/'); int best = 0; string chosen = null; bool tie = false;
   foreach (string local in matches) {
    string[] localParts = Normalize(local).Split('/'); int score = 0;
    while (score < Math.Min(parts.Length, localParts.Length) && String.Equals(parts[parts.Length-1-score], localParts[localParts.Length-1-score], StringComparison.OrdinalIgnoreCase)) score++;
    if (score > best) { best = score; chosen = local; tie = false; } else if (score == best) tie = true;
   }
   return best > 1 && !tie ? chosen : null;
  }
  public static ImportResult Scan(string root) {
   root = System.IO.Path.GetFullPath(root);
   if (!Directory.Exists(root)) throw new DirectoryNotFoundException("That folder is no longer available.");
   var result = new ImportResult();
   var files = Walk(root, result.Warnings).OrderBy(x=>x, StringComparer.OrdinalIgnoreCase).ToList();
   var songs = new HashSet<string>(files.Where(x=>Extensions.Contains(System.IO.Path.GetExtension(x))), StringComparer.OrdinalIgnoreCase);
   var names = songs.GroupBy(x=>System.IO.Path.GetFileName(x), StringComparer.OrdinalIgnoreCase).ToDictionary(x=>x.Key,x=>x.ToList(),StringComparer.OrdinalIgnoreCase);
   result.SongCount = songs.Count;
   var lists = files.Where(x=>String.Equals(System.IO.Path.GetExtension(x), ".m3u",StringComparison.OrdinalIgnoreCase) || String.Equals(System.IO.Path.GetExtension(x), ".m3u8",StringComparison.OrdinalIgnoreCase));
   foreach (string file in lists) {
    var p = new Playlist { Id = Guid.NewGuid().ToString(), Name = System.IO.Path.GetFileNameWithoutExtension(file), Source = file, Root = root };
    try {
     byte[] bytes = File.ReadAllBytes(file); string content;
     try { content = new UTF8Encoding(false,true).GetString(bytes); } catch (DecoderFallbackException) { content = Encoding.Default.GetString(bytes); }
     content = content.TrimStart('\uFEFF'); string title = null;
     foreach (string line in content.Split(new [] { '\r','\n' }, StringSplitOptions.RemoveEmptyEntries)) {
      string entry = line.Trim(); if (entry.Length == 0) continue;
      if (entry.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase)) { int comma = entry.IndexOf(','); title = comma >= 0 ? entry.Substring(comma+1).Trim() : null; continue; }
      if (entry.StartsWith("#")) continue;
      string local = Resolve(entry, file, root, songs, names);
      if (local == null) p.Unmatched.Add(entry); else p.Tracks.Add(new Track { Path = local, Title = String.IsNullOrWhiteSpace(title) ? System.IO.Path.GetFileNameWithoutExtension(local) : title });
      title = null;
     }
     result.Playlists.Add(p);
    } catch (Exception e) { if (!(e is IOException || e is UnauthorizedAccessException)) throw; result.Warnings.Add("Could not read playlist: " + file); }
   }
   if (result.Playlists.Count == 0 && songs.Count > 0) result.Playlists.Add(new Playlist {
    Id = Guid.NewGuid().ToString(), Name = new DirectoryInfo(root).Name, Source = root, Root = root,
    Tracks = songs.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).Select(x=>new Track { Path = x, Title = System.IO.Path.GetFileNameWithoutExtension(x) }).ToList()
   });
   return result;
  }
 }
 public class PlaybackOrder {
  readonly Random random; List<int> bag = new List<int>(); List<int> history = new List<int>(); int cursor = -1; int count; bool shuffled;
  public int Current { get { return cursor >= 0 ? history[cursor] : -1; } }
  public PlaybackOrder() : this(new Random()) {} public PlaybackOrder(Random r) { random = r; }
  public void Reset(int n, bool shuffle, int initial) { count = n; shuffled = shuffle; history.Clear(); cursor = -1; bag.Clear(); if (initial >= 0 && initial < count) { history.Add(initial); cursor = 0; } Fill(Current); }
  void Fill(int excluded) { bag = Enumerable.Range(0,count).Where(x=>x!=excluded).ToList(); for (int i=bag.Count-1;i>0;i--) { int j=random.Next(i+1); int temp=bag[i]; bag[i]=bag[j]; bag[j]=temp; } }
  public int Next() {
   if (count == 0) return -1;
   if (cursor+1 < history.Count) return history[++cursor];
   int next;
   if (!shuffled) next = (Current+1)%count;
   else { if (bag.Count == 0) Fill(-1); if (bag.Count > 1 && bag[bag.Count-1] == Current) { int t=bag[0]; bag[0]=bag[bag.Count-1]; bag[bag.Count-1]=t; } next = bag[bag.Count-1]; bag.RemoveAt(bag.Count-1); }
   history.Add(next); cursor++; if (history.Count > 2000) { history.RemoveAt(0); cursor--; } return next;
  }
  public int Previous() { if (cursor > 0) return history[--cursor]; return Current; }
 }
 public class Player {
  Window window; Settings settings; string dataDir; Playlist active; readonly MediaPlayer media = new MediaPlayer(); readonly PlaybackOrder order = new PlaybackOrder();
  Button play, previous, next, import; TextBlock title, subtitle, status, elapsed, duration, playlistTitle, trackCount, playState;
  CheckBox shuffle, greeting; Slider seek, volume; ListBox tracks; DispatcherTimer timer, saveTimer;
  bool playing, opened, opening, dragging, updating, closing, busy; double pendingPosition; int failures; SpeechSynthesizer voice;
  ListBox lyricLines; TextBlock lyricNote, lyricOffset; LyricDocument lyrics; int lyricIndex=-2; string lyricTrack;
  DispatcherTimer autoLyricsTimer; CancellationTokenSource lyricCancellation; int lyricGeneration; string attemptedLyricsPath;
  public Player(string directory) {
   dataDir = directory; settings = Storage.LoadForPlayback(directory);
   using (var stream = typeof(Player).Assembly.GetManifestResourceStream("Player.xaml")) window = (Window)XamlReader.Load(stream);
   play = Get<Button>("PlayButton"); previous = Get<Button>("PreviousButton"); next = Get<Button>("NextButton"); import = Get<Button>("ImportButton");
   title = Get<TextBlock>("SongTitle"); subtitle = Get<TextBlock>("SongSubtitle"); status = Get<TextBlock>("Status"); elapsed = Get<TextBlock>("Elapsed"); duration = Get<TextBlock>("Duration");
   playlistTitle = Get<TextBlock>("PlaylistTitle"); trackCount = Get<TextBlock>("TrackCount"); playState = Get<TextBlock>("PlayState");
   tracks = Get<ListBox>("Tracks"); seek = Get<Slider>("Seek"); volume = Get<Slider>("Volume"); shuffle = Get<CheckBox>("Shuffle"); greeting = Get<CheckBox>("Greeting");
   lyricLines=Get<ListBox>("LyricLines"); lyricNote=Get<TextBlock>("LyricNote"); lyricOffset=Get<TextBlock>("LyricOffset");
   var autoLyrics=Get<CheckBox>("AutomaticLyrics"); autoLyrics.IsChecked=settings.AutomaticLyrics;
   autoLyricsTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(650) };
   autoLyricsTimer.Tick+=delegate { autoLyricsTimer.Stop(); FetchAutomaticLyrics(); };
   autoLyrics.Click+=delegate { settings.AutomaticLyrics=autoLyrics.IsChecked==true; CancelLyricsLookup(); attemptedLyricsPath=null; if(settings.AutomaticLyrics) ScheduleLyricsLookup(); else if(lyrics==null) lyricNote.Text="Automatic lookup is off. Find lyrics or load an LRC file."; SaveSoon(); };
   Get<Button>("LyricsButton").Click+=delegate { settings.ShowLyrics=!settings.ShowLyrics; LyricsVisibility(); SaveSoon(); };
   Get<Button>("FindLyricsButton").Click+=delegate { FindLyrics(); };
   Get<Button>("LoadLyricsButton").Click+=delegate { LoadLyricsFile(); };
   Get<Button>("EarlierLyricsButton").Click+=delegate { settings.LyricsOffset=Math.Max(-60,settings.LyricsOffset-0.5); UpdateLyricOffset(); UpdateLyrics(); SaveSoon(); };
   Get<Button>("LaterLyricsButton").Click+=delegate { settings.LyricsOffset=Math.Min(60,settings.LyricsOffset+0.5); UpdateLyricOffset(); UpdateLyrics(); SaveSoon(); };
   Get<Button>("ResetLyricsButton").Click+=delegate { settings.LyricsOffset=0; UpdateLyricOffset(); UpdateLyrics(); SaveSoon(); };
   LyricsVisibility(); UpdateLyricOffset();
   shuffle.IsChecked = settings.Shuffle; greeting.IsChecked = settings.Greeting; volume.Value = settings.Volume; media.Volume = settings.Volume;
   Get<Button>("PlaylistsButton").Click += delegate { ShowPlaylists(); }; import.Click += delegate { ChooseFolder(); }; Get<Button>("HelpButton").Click += delegate { Help(); };
   play.Click += delegate { Toggle(); }; next.Click += delegate { failures=0; PlayIndex(order.Next(), true, 0); }; previous.Click += delegate { failures=0; if (opened && media.Position.TotalSeconds>3) media.Position=TimeSpan.Zero; else PlayIndex(order.Previous(), true, 0); };
   tracks.MouseDoubleClick += delegate { if (tracks.SelectedIndex >= 0) { order.Reset(active.Tracks.Count,settings.Shuffle,tracks.SelectedIndex); failures=0; PlayIndex(order.Current, true, 0); } };
   shuffle.Click += delegate { settings.Shuffle = shuffle.IsChecked == true; if (active!=null) order.Reset(active.Tracks.Count,settings.Shuffle,order.Current); SaveSoon(); };
   greeting.Click += delegate { settings.Greeting=greeting.IsChecked==true; SaveSoon(); if(settings.Greeting) Speak(); else if(voice!=null) voice.SpeakAsyncCancelAll(); };
   volume.ValueChanged += delegate { media.Volume=volume.Value; settings.Volume=volume.Value; SaveSoon(); };
   seek.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(delegate { dragging=true; }));
   seek.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(delegate { dragging=false; SetPosition(); }));
   seek.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (opened && !(e.OriginalSource is Thumb)) { dragging=true; seek.Value=seek.Maximum*Math.Max(0,Math.Min(1,e.GetPosition(seek).X/seek.ActualWidth)); seek.CaptureMouse(); e.Handled=true; } };
   seek.PreviewMouseMove += delegate(object sender, MouseEventArgs e) { if(dragging && seek.IsMouseCaptured && Mouse.LeftButton==MouseButtonState.Pressed) { seek.Value=seek.Maximum*Math.Max(0,Math.Min(1,e.GetPosition(seek).X/seek.ActualWidth)); e.Handled=true; } };
   seek.PreviewMouseLeftButtonUp += delegate { if(seek.IsMouseCaptured) { seek.ReleaseMouseCapture(); dragging=false; SetPosition(); } };
   seek.ValueChanged += delegate { if (!updating && !dragging && opened) SetPosition(); if(dragging) elapsed.Text=Time(seek.Value); };
   media.MediaOpened += delegate {
    opening=false; opened=true; failures=0; seek.IsEnabled=media.NaturalDuration.HasTimeSpan;
    updating=true; seek.Maximum=media.NaturalDuration.HasTimeSpan ? Math.Max(0.01,media.NaturalDuration.TimeSpan.TotalSeconds) : 1; duration.Text=media.NaturalDuration.HasTimeSpan ? Time(seek.Maximum) : "—"; updating=false;
    if(pendingPosition>0 && media.NaturalDuration.HasTimeSpan) media.Position=TimeSpan.FromSeconds(Math.Min(pendingPosition,Math.Max(0,seek.Maximum-0.1)));
    if(playing) media.Play(); else media.Pause(); Controls(); ScheduleLyricsLookup();
   };
   media.MediaEnded += delegate { if (!closing && playing) { failures=0; PlayIndex(order.Next(),true,0); } };
   media.MediaFailed += delegate(object sender, ExceptionEventArgs e) { opening=false; opened=false; if(!closing) Failed("Windows could not play this file: " + (active!=null && order.Current>=0 ? active.Tracks[order.Current].Title : "unknown song")); };
   timer = new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(100) }; timer.Tick += delegate { if(opened && !dragging) { updating=true; seek.Value=media.Position.TotalSeconds; elapsed.Text=Time(seek.Value); updating=false; } UpdateLyrics(); }; timer.Start();
   saveTimer = new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(500) }; saveTimer.Tick += delegate { saveTimer.Stop(); Save(); };
   window.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if(e.Key==Key.Space && !(Keyboard.FocusedElement is Button || Keyboard.FocusedElement is CheckBox || Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is Slider)) { Toggle(); e.Handled=true; } };
   window.Closing += delegate { closing=true; CancelLyricsLookup(); timer.Stop(); saveTimer.Stop(); Save(); media.Close(); if(voice!=null) { voice.SpeakAsyncCancelAll(); voice.Dispose(); } };
   window.ContentRendered += delegate { if(settings.Greeting) Speak(); };
   active = settings.Playlists.FirstOrDefault(x=>x.Id==settings.ActiveId);
   if(active==null || active.Tracks.Count==0) active=settings.Playlists.FirstOrDefault(x=>x.Tracks.Count>0) ?? active;
   if(active!=null) SelectPlaylist(active,true,-1,0);
   if(Storage.Warning!=null) status.Text=Storage.Warning;
  }
  T Get<T>(string name) where T:class { return window.FindName(name) as T; }
  public void Show() { window.Show(); }
  static string Time(double seconds) { var t=TimeSpan.FromSeconds(Math.Max(0,seconds)); return t.TotalHours>=1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss"); }
  void Controls() { bool has=active!=null && active.Tracks.Count>0; play.IsEnabled=previous.IsEnabled=next.IsEnabled=has; play.Content=playing ? "Pause" : "Play"; playState.Text=opening ? "Opening song…" : playing ? "Playing" : "Paused"; }
  void SetPosition() { if(opened && media.NaturalDuration.HasTimeSpan) { media.Position=TimeSpan.FromSeconds(Math.Min(seek.Value,seek.Maximum)); settings.Position=seek.Value; UpdateLyrics(); SaveSoon(); } }
  void Toggle() {
   if(active==null || active.Tracks.Count==0) { ShowPlaylists(); return; }
   if(order.Current<0) { failures=0; PlayIndex(order.Next(),true,0); return; }
   if(!opened && !opening) { failures=0; PlayIndex(order.Current,true,0); return; }
   playing=!playing; if(playing) media.Play(); else media.Pause(); Controls(); SaveSoon();
  }
  void SelectPlaylist(Playlist p, bool auto, int index, double position) {
   media.Close(); active=p; settings.ActiveId=p.Id; playing=opened=opening=false; pendingPosition=0;
   SetLyrics(null,null);
   playlistTitle.Text=p.Name; trackCount.Text=p.Tracks.Count+" songs"; tracks.ItemsSource=p.Tracks; order.Reset(p.Tracks.Count,settings.Shuffle,index);
   title.Text=p.Name; subtitle.Text=p.Tracks.Count+" songs · "+(settings.Shuffle ? "Shuffle is on" : "In playlist order"); elapsed.Text=duration.Text="0:00"; updating=true; seek.Value=0; seek.IsEnabled=false; updating=false; Controls();
   status.Text=p.Unmatched.Count>0 ? p.Unmatched.Count+" entries could not be matched. Open Playlists → Import details to review." : "Double-click a song to play it. Your copied music files stay in their original folder.";
   failures=0; if(auto) PlayIndex(index>=0 ? index : order.Next(),true,0); else if(index>=0) PlayIndex(index,false,position); SaveSoon();
  }
  void PlayIndex(int index, bool auto, double position) {
   if(active==null || index<0 || index>=active.Tracks.Count) return;
   media.Close(); opened=opening=false; playing=auto; pendingPosition=position; updating=true; seek.Value=0; seek.IsEnabled=false; updating=false; elapsed.Text=duration.Text="0:00";
   Track t=active.Tracks[index]; title.Text=t.Title; subtitle.Text=active.Name+" · "+t.Folder; tracks.SelectedIndex=index; tracks.ScrollIntoView(t); settings.CurrentPath=t.Path; settings.Position=position;
   try { SetLyrics(LyricsStore.Load(dataDir,t.Path),t.Path); } catch(Exception e) { SetLyrics(null,t.Path); lyricNote.Text="Could not read lyrics: "+e.Message; }
   if(!File.Exists(t.Path)) { Failed("Song is missing: "+t.Title+". Copy it back, or add the folder again to refresh."); return; }
   try { opening=true; media.Open(new Uri(t.Path,UriKind.Absolute)); if(auto) media.Play(); Controls(); SaveSoon(); }
   catch(Exception e) { opening=false; Failed("Could not open this song: "+e.Message); }
  }
  void Failed(string message) {
   status.Text=message; failures++; opened=opening=false;
   if(playing && active!=null && failures<active.Tracks.Count) window.Dispatcher.BeginInvoke(new Action(delegate { PlayIndex(order.Next(),true,0); }));
   else { playing=false; media.Close(); Controls(); }
  }
  void SaveSoon() { if(saveTimer!=null && !closing) { saveTimer.Stop(); saveTimer.Start(); } }
  void Save() {
   if(opened) settings.Position=media.Position.TotalSeconds;
   try { Storage.Save(dataDir,settings); } catch(Exception e) { status.Text="Could not save settings: "+e.Message; }
  }
  void Speak() {
   try {
    if(voice==null) { voice=new SpeechSynthesizer(); var english=voice.GetInstalledVoices().FirstOrDefault(x=>x.Enabled && x.VoiceInfo.Culture.TwoLetterISOLanguageName=="en"); if(english!=null) voice.SelectVoice(english.VoiceInfo.Name); }
    voice.SpeakAsyncCancelAll(); voice.SpeakAsync("Tiago, which playlist would you like to listen to?");
   } catch(Exception) { status.Text="Voice greeting is unavailable on this PC. Music playback is still available."; }
  }
  void LyricsVisibility() {
   Get<FrameworkElement>("LyricsPanel").Visibility=settings.ShowLyrics ? Visibility.Visible : Visibility.Collapsed;
   Get<ColumnDefinition>("LyricsColumn").Width=settings.ShowLyrics ? new GridLength(1,GridUnitType.Star) : new GridLength(0);
   window.Width=settings.ShowLyrics ? 800 : 450; window.MinWidth=settings.ShowLyrics ? 720 : 420;
   Get<Button>("LyricsButton").Content=settings.ShowLyrics ? "Hide lyrics" : "Lyrics";
  }
  void UpdateLyricOffset() { lyricOffset.Text=(settings.LyricsOffset>=0 ? "+" : "")+settings.LyricsOffset.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"s"; }
  void SetLyrics(LyricDocument document,string path) {
   CancelLyricsLookup(); if(!String.Equals(lyricTrack,path,StringComparison.OrdinalIgnoreCase)) attemptedLyricsPath=null;
   lyrics=document; lyricTrack=path; lyricIndex=-2; lyricLines.ItemsSource=document==null ? null : document.Lines;
   lyricNote.Text=document==null ? path==null ? "Choose a song. Lyrics load automatically; saved lyrics work offline." : settings.AutomaticLyrics ? "Lyrics will load automatically…" : "Find lyrics or load a local LRC file." : document.Timed ? "Timed lyrics · "+document.Source : "Plain lyrics · no timing available";
   Get<Button>("FindLyricsButton").IsEnabled=Get<Button>("LoadLyricsButton").IsEnabled=path!=null;
   UpdateLyrics();
  }
  void CancelLyricsLookup() {
   lyricGeneration++; if(autoLyricsTimer!=null) autoLyricsTimer.Stop();
   if(lyricCancellation!=null) { lyricCancellation.Cancel(); lyricCancellation=null; }
  }
  void ScheduleLyricsLookup() {
   if(closing || !settings.AutomaticLyrics || lyrics!=null || lyricTrack==null || !opened || attemptedLyricsPath==lyricTrack) return;
   lyricNote.Text="Finding lyrics automatically…"; autoLyricsTimer.Stop(); autoLyricsTimer.Start();
  }
  void FetchAutomaticLyrics() {
   if(closing || !settings.AutomaticLyrics || lyrics!=null || lyricTrack==null || !opened || attemptedLyricsPath==lyricTrack) return;
   string song=lyricTrack; attemptedLyricsPath=song; int generation=lyricGeneration;
   double length=media.NaturalDuration.HasTimeSpan ? media.NaturalDuration.TimeSpan.TotalSeconds : 0;
   var identity=SongIdentity.FromName(Path.GetFileNameWithoutExtension(song));
   var cancellation=new CancellationTokenSource(); lyricCancellation=cancellation;
   lyricNote.Text="Finding lyrics automatically…";
   Task.Factory.StartNew(delegate { cancellation.Token.ThrowIfCancellationRequested(); return identity.Match(LyricsStore.Search(identity.Query,length,cancellation.Token),length); }).ContinueWith(task=> {
    // Observe errors even when a song change cancels a lookup.
    Exception failure=task.IsFaulted ? task.Exception.GetBaseException() : null;
    if(window.Dispatcher.HasShutdownStarted) { cancellation.Dispose(); return; }
    window.Dispatcher.BeginInvoke(new Action(delegate {
     bool current=!closing && generation==lyricGeneration && song==lyricTrack && !cancellation.IsCancellationRequested;
     if(lyricCancellation==cancellation) lyricCancellation=null;
     if(current) {
      if(failure!=null) {
       lyricNote.Text="Lyrics lookup failed: "+failure.Message;
       try { File.WriteAllText(Path.Combine(dataDir,"lyrics-diagnostics.txt"),failure.ToString()); } catch(Exception) {}
      }
      else if(task.Result==null) lyricNote.Text="No matching lyrics found. Find lyrics can choose another version.";
      else if(task.Result.instrumental) lyricNote.Text="This recording is listed as instrumental.";
      else try {
       var record=task.Result; bool timed=!String.IsNullOrWhiteSpace(record.syncedLyrics); LyricsStore.Save(dataDir,song,timed ? record.syncedLyrics : record.plainLyrics,timed);
       var doc=LyricsStore.Load(dataDir,song); doc.Source="LRCLIB · saved offline"; SetLyrics(doc,song);
      } catch(Exception e) { lyricNote.Text="Could not save lyrics: "+e.Message; }
     }
     cancellation.Dispose();
    }));
   });
  }
  void UpdateLyrics() {
   if(lyrics==null || !lyrics.Timed) return;
   double position=opened ? (dragging ? seek.Value : media.Position.TotalSeconds) : pendingPosition;
   // A positive adjustment delays the line highlight, matching an extra intro in the audio.
   int index=lyrics.At(position-settings.LyricsOffset);
   if(index==lyricIndex) return; lyricIndex=index; lyricLines.SelectedIndex=index;
   if(index>=0) { lyricLines.ScrollIntoView(lyrics.Lines[Math.Min(index+2,lyrics.Lines.Count-1)]); lyricLines.ScrollIntoView(lyrics.Lines[index]); }
  }
  void LoadLyricsFile() {
   if(lyricTrack==null) return; string song=lyricTrack;
   var dialog=new Microsoft.Win32.OpenFileDialog { Title="Load lyrics for the current song", Filter="Lyric files (*.lrc;*.txt)|*.lrc;*.txt", CheckFileExists=true };
   if(dialog.ShowDialog(window)!=true) return;
   try {
    if(new FileInfo(dialog.FileName).Length>1024*1024) throw new IOException("Lyric file is too large (maximum 1 MB).");
    string content=File.ReadAllText(dialog.FileName); var parsed=LyricDocument.Parse(content); LyricsStore.Save(dataDir,song,content,parsed.Timed);
    if(song==lyricTrack) SetLyrics(LyricsStore.Load(dataDir,song),song);
   } catch(Exception e) { MessageBox.Show(window,"Could not load lyrics: "+e.Message,"Lyrics"); }
  }
  void FindLyrics() {
   if(lyricTrack==null) return; string song=lyricTrack;
   var dialog=new Window { Title="Find lyrics · Tiago Music", Width=620, Height=510, MinWidth=520, MinHeight=420, Owner=window, WindowStartupLocation=WindowStartupLocation.CenterOwner, Background=window.Background, FontFamily=window.FontFamily, FontSize=14 }; dialog.Resources=window.Resources;
   var grid=new Grid { Margin=new Thickness(20) }; for(int i=0;i<5;i++) grid.RowDefinitions.Add(new RowDefinition { Height=i==3 ? new GridLength(1,GridUnitType.Star) : GridLength.Auto });
   var heading=new TextBlock { Text="Find lyrics for this song", FontSize=22, FontWeight=FontWeights.SemiBold, Margin=new Thickness(0,0,0,10) }; grid.Children.Add(heading);
   string query=System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(song),@"\s*[\(\[].*?[\)\]]","").Replace('_',' ').Trim();
   var searchBar=new Grid { Margin=new Thickness(0,0,0,10) }; searchBar.ColumnDefinitions.Add(new ColumnDefinition()); searchBar.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); Grid.SetRow(searchBar,1); grid.Children.Add(searchBar);
   var input=new TextBox { Text=query, FontSize=16, Padding=new Thickness(8), Margin=new Thickness(0,0,8,0) }; searchBar.Children.Add(input);
   var note=new TextBlock { Text="Search sends this text to LRCLIB. Select the correct version; timed lyrics are preferred. Your audio stays on this PC. The Auto checkbox controls automatic song lookups.", TextWrapping=TextWrapping.Wrap, Foreground=Brushes.SlateGray, Margin=new Thickness(0,0,0,12) }; Grid.SetRow(note,2); grid.Children.Add(note);
   var list=new ListBox { BorderBrush=Brushes.LightGray }; Grid.SetRow(list,3); grid.Children.Add(list);
   var bottom=new WrapPanel { Margin=new Thickness(0,12,0,0) }; Grid.SetRow(bottom,4); grid.Children.Add(bottom);
   bool searching=false, dialogClosed=false;
   var use=MakeButton("Use selected lyrics",delegate {
    var record=list.SelectedItem as LyricsRecord; if(record==null) return;
    if(record.instrumental) { note.Text="This version is marked instrumental. No lyrics are available."; return; }
    try {
     bool timed=!String.IsNullOrWhiteSpace(record.syncedLyrics); LyricsStore.Save(dataDir,song,timed ? record.syncedLyrics : record.plainLyrics,timed);
     if(song==lyricTrack) { var doc=LyricsStore.Load(dataDir,song); doc.Source="LRCLIB · saved offline"; SetLyrics(doc,song); }
     dialog.Close();
    } catch(Exception e) { note.Text="Could not save lyrics: "+e.Message; }
   }); use.IsEnabled=false; bottom.Children.Add(use); bottom.Children.Add(MakeButton("Cancel",delegate { dialog.Close(); }));
   list.SelectionChanged+=delegate { use.IsEnabled=list.SelectedItem!=null; };
   Button search=null;
   Action begin=delegate {
    if(searching || String.IsNullOrWhiteSpace(input.Text)) return;
    searching=true; search.IsEnabled=false; use.IsEnabled=false; string text=input.Text; double length=opened && media.NaturalDuration.HasTimeSpan ? media.NaturalDuration.TimeSpan.TotalSeconds : 0;
    note.Text="Searching LRCLIB…"; list.ItemsSource=null;
    Task.Factory.StartNew(()=>LyricsStore.Search(text,length)).ContinueWith(task=>window.Dispatcher.BeginInvoke(new Action(delegate {
     if(closing || dialogClosed) return; searching=false; search.IsEnabled=true;
     if(task.IsFaulted) { note.Text=task.Exception.GetBaseException().Message; return; }
     list.ItemsSource=task.Result; note.Text=task.Result.Count==0 ? "No lyrics found. Try just the artist and song title, or load a local LRC file." : "Select the version that matches your recording. Timed lyrics highlight the current line; plain lyrics do not.";
    })));
   };
   search=MakeButton("Search",begin); Grid.SetColumn(search,1); searchBar.Children.Add(search); input.KeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.Enter) begin(); };
   dialog.Closed+=delegate { dialogClosed=true; }; dialog.Content=grid; dialog.ShowDialog();
  }
  void ChooseFolder() {
   if(busy) return;
   using(var dialog=new Forms.FolderBrowserDialog { Description="Choose a folder on this PC containing copied songs and exported M3U/M3U8 playlists.", ShowNewFolderButton=false }) {
    if(dialog.ShowDialog()!=Forms.DialogResult.OK) return;
    ScanFolder(dialog.SelectedPath);
   }
  }
  void ScanFolder(string folder) {
   busy=true; import.IsEnabled=false; status.Text="Looking for songs and playlists…";
   Task.Factory.StartNew(()=>Importer.Scan(folder)).ContinueWith(task=>window.Dispatcher.BeginInvoke(new Action(delegate {
    busy=false; import.IsEnabled=true; if(closing) return;
    if(task.IsFaulted) { status.Text="Could not import: "+task.Exception.GetBaseException().Message; return; }
    var result=task.Result;
    if(result.Playlists.Count==0) { MessageBox.Show(window,"No MP3, WAV, WMA or M4A songs or M3U/M3U8 playlists were found. Choose the copied music folder on this PC.","No music found"); return; }
    if(result.Playlists.Count==1 && Directory.Exists(result.Playlists[0].Source)) {
     if(MessageBox.Show(window,"Found "+result.SongCount+" songs and no exported playlists. Add this folder as a playlist? Shuffle is "+(settings.Shuffle ? "on" : "off")+".\n\nCopying songs alone does not preserve Samsung Music's custom playlists.","Add folder playlist",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) { status.Text="Folder was not added."; return; }
    }
    foreach(var p in result.Playlists) {
     var existing=settings.Playlists.FirstOrDefault(x=>String.Equals(x.Source,p.Source,StringComparison.OrdinalIgnoreCase));
     if(existing!=null) { p.Id=existing.Id; p.Name=existing.Name; settings.Playlists[settings.Playlists.IndexOf(existing)]=p; if(active!=null && active.Id==p.Id) SelectPlaylist(p,false,-1,0); }
     else settings.Playlists.Add(p);
    }
    if(active==null && result.Playlists.Count>0) SelectPlaylist(result.Playlists[0],false,-1,0);
    Save(); int missing=result.Playlists.Sum(x=>x.Unmatched.Count);
    status.Text="Added or refreshed "+result.Playlists.Count+" playlist(s); "+result.SongCount+" local songs found"+(missing>0 ? "; "+missing+" unmatched entries." : ".");
    MessageBox.Show(window,status.Text+"\n\n"+(missing>0 ? "Open Playlists → Import details to review missing or ambiguous entries.\n\n" : "")+String.Join("\n",result.Warnings.Take(8))+"\nYour music and playlist files were left intact.","Import complete");
    ShowPlaylists();
   })));
  }
  Button MakeButton(string label, Action action) { var b=new Button { Content=label, Padding=new Thickness(14,9,14,9), Margin=new Thickness(0,0,8,0) }; b.Click+=delegate { action(); }; return b; }
  void ShowPlaylists() {
   var dialog=new Window { Title="Playlists · Tiago Music", Width=620, Height=460, MinWidth=540, MinHeight=380, Owner=window, WindowStartupLocation=WindowStartupLocation.CenterOwner, Background=window.Background, FontFamily=window.FontFamily, FontSize=14 };
   dialog.Resources=window.Resources;
   var grid=new Grid { Margin=new Thickness(22) }; grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
   var heading=new TextBlock { Text="Choose your playlist", FontSize=24, FontWeight=FontWeights.SemiBold, Margin=new Thickness(0,0,0,16) }; grid.Children.Add(heading);
   var list=new ListBox { ItemsSource=settings.Playlists, BorderBrush=Brushes.LightGray }; Grid.SetRow(list,1); grid.Children.Add(list); list.SelectedItem=active;
   var note=new TextBlock { Text=settings.Playlists.Count==0 ? "No saved playlists yet. Add a music folder to get started." : "Select a playlist, then press Play. Double-click also starts playback.", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,12,0,12), Foreground=Brushes.SlateGray }; Grid.SetRow(note,2); grid.Children.Add(note);
   var buttons=new WrapPanel(); Grid.SetRow(buttons,3); grid.Children.Add(buttons);
   Action start=delegate { var p=list.SelectedItem as Playlist; if(p==null) return; if(p.Tracks.Count==0) { MessageBox.Show(dialog,"This playlist has no matched local songs. Use Import details to see which entries need attention.","No playable songs"); return; } dialog.Close(); SelectPlaylist(p,true,-1,0); };
   buttons.Children.Add(MakeButton("Play",start)); list.MouseDoubleClick+=delegate { start(); };
   buttons.Children.Add(MakeButton("Add folder",delegate { dialog.Close(); ChooseFolder(); }));
   buttons.Children.Add(MakeButton("Rename",delegate { var p=list.SelectedItem as Playlist; if(p==null) return; string value=AskName(dialog,p.Name); if(!String.IsNullOrWhiteSpace(value)) { p.Name=value.Trim(); list.Items.Refresh(); if(active!=null && active.Id==p.Id) { playlistTitle.Text=p.Name; if(order.Current>=0) subtitle.Text=p.Name+" · "+active.Tracks[order.Current].Folder; } Save(); } }));
   buttons.Children.Add(MakeButton("Remove",delegate { var p=list.SelectedItem as Playlist; if(p==null) return; if(MessageBox.Show(dialog,"Remove “"+p.Name+"” from Tiago Music?\n\nYour songs and original playlist file will stay intact.","Remove saved playlist",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return; settings.Playlists.Remove(p); list.Items.Refresh(); if(active!=null && active.Id==p.Id) { media.Close(); active=null; settings.ActiveId=null; settings.CurrentPath=null; settings.Position=0; order.Reset(0,settings.Shuffle,-1); tracks.ItemsSource=null; playing=opened=opening=false; title.Text="Tiago, choose a playlist"; subtitle.Text="Open Playlists, or add a music folder."; playlistTitle.Text="Your listening queue"; trackCount.Text="0 songs"; seek.IsEnabled=false; updating=true; seek.Value=0; updating=false; elapsed.Text=duration.Text="0:00"; Controls(); } Save(); }));
   buttons.Children.Add(MakeButton("Import details",delegate { var p=list.SelectedItem as Playlist; if(p==null) return; Details(dialog,p); }));
   dialog.Content=grid; dialog.ShowDialog();
  }
  string AskName(Window owner,string original) {
   var w=new Window { Title="Rename playlist", Width=410, Height=170, ResizeMode=ResizeMode.NoResize, Owner=owner, WindowStartupLocation=WindowStartupLocation.CenterOwner }; var panel=new StackPanel { Margin=new Thickness(20) }; var input=new TextBox { Text=original, FontSize=16, Margin=new Thickness(0,0,0,16) }; panel.Children.Add(input); string value=null;
   var ok=MakeButton("Save",delegate { value=input.Text; w.DialogResult=true; }); ok.IsDefault=true; panel.Children.Add(ok); w.Content=panel; w.Loaded+=delegate { input.Focus(); input.SelectAll(); }; w.ShowDialog(); return value;
  }
  void Details(Window owner,Playlist p) {
   var w=new Window { Title="Import details · "+p.Name, Width=640, Height=450, Owner=owner, WindowStartupLocation=WindowStartupLocation.CenterOwner }; var panel=new DockPanel { Margin=new Thickness(20) };
   var info=new TextBlock { Text="Source: "+p.Source+"\n\n"+p.Tracks.Count+" matched songs; "+p.Unmatched.Count+" unmatched entries.\nMissing songs and duplicate filenames without a clear folder match are left unresolved. Add the folder again after fixing them.", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,0,0,16) }; DockPanel.SetDock(info,Dock.Top); panel.Children.Add(info);
   var text=new TextBox { Text=p.Unmatched.Count==0 ? "All playlist entries were matched." : String.Join(Environment.NewLine,p.Unmatched), IsReadOnly=true, AcceptsReturn=true, TextWrapping=TextWrapping.Wrap, VerticalScrollBarVisibility=ScrollBarVisibility.Auto }; panel.Children.Add(text); w.Content=panel; w.ShowDialog();
  }
  void Help() {
   var w=new Window { Title="From your Samsung phone · Tiago Music", Width=650, Height=560, Owner=window, WindowStartupLocation=WindowStartupLocation.CenterOwner, Background=window.Background, FontFamily=window.FontFamily }; w.Resources=window.Resources;
   var panel=new StackPanel { Margin=new Thickness(24) }; panel.Children.Add(new TextBlock { Text="Bring your music over with USB", FontSize=24, FontWeight=FontWeights.SemiBold, Margin=new Thickness(0,0,0,20) });
   panel.Children.Add(new TextBlock { FontSize=15, TextWrapping=TextWrapping.Wrap, Text="1. On your phone, open Samsung Music → menu (⋮) → Settings → Manage playlists → Export my playlists. Menu names can vary by version.\n\n2. Connect your unlocked phone with a USB data cable. On the phone, choose File transfer / Android Auto if asked.\n\n3. In Windows File Explorer, open your phone and COPY the music folder(s) and exported .m3u / .m3u8 files into a folder on this PC. Keep the album folders together where possible. Copy, rather than move, to keep the phone originals.\n\n4. In Tiago Music, choose Add music folder and select that PC folder. Playlists are discovered automatically. Phone paths are matched to local songs; ambiguous or missing entries appear in Import details.\n\nOnly copied songs? Tiago Music can add the folder as a playlist. Copying songs alone does not preserve custom playlists saved only inside Samsung Music. Export those playlists first when available.\n\nKeep the copied folder on this PC. Tiago Music references those files; it does not duplicate or delete them. No account or internet is required to listen." });
   panel.Children.Add(new TextBlock { Text="Samsung instructions: samsung.com/levant/support/apps-services/how-to-import-and-export-samsung-music-playlists/", FontSize=11, Foreground=Brushes.SlateGray, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,18,0,0) }); var scroll=new ScrollViewer { Content=panel, VerticalScrollBarVisibility=ScrollBarVisibility.Auto }; w.Content=scroll; w.ShowDialog();
  }
 }
 public static class Program {
  [STAThread] public static int Main(string[] args) {
   if(args.Length>=2 && args[0]=="--self-test") return Tests.Run(args[1]);
   if(args.Length>=3 && args[0]=="--audio-test") return Tests.Audio(args[1],args[2]);
   if(args.Length>=4 && args[0]=="--auto-lyrics-test") return Tests.AutoLyrics(args[1],Double.Parse(args[2],System.Globalization.CultureInfo.InvariantCulture),args[3]);
   if(args.Length>=4 && args[0]=="--import-folders") {
    try {
     var s=Storage.Load(args[2]); var report=new List<string>();
     foreach(string folder in Directory.GetDirectories(args[1]).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase)) {
     var result=Importer.Scan(folder);
      if(result.Playlists.Count==0) result.Playlists.Add(new Playlist { Id=Guid.NewGuid().ToString(), Name=new DirectoryInfo(folder).Name, Source=folder, Root=folder });
      foreach(var p in result.Playlists) { var existing=s.Playlists.FirstOrDefault(x=>String.Equals(x.Source,p.Source,StringComparison.OrdinalIgnoreCase)); if(existing!=null) { p.Id=existing.Id; p.Name=existing.Name; s.Playlists[s.Playlists.IndexOf(existing)]=p; } else s.Playlists.Add(p); report.Add(p.ToString()); }
      report.AddRange(result.Warnings);
     }
     if(s.ActiveId==null && s.Playlists.Count>0) s.ActiveId=s.Playlists[0].Id;
     Storage.Save(args[2],s); File.WriteAllLines(args[3],report); return 0;
    } catch(Exception e) { File.WriteAllText(args[3],e.ToString()); return 1; }
   }
   // Keep one portable library beside this executable. This avoids Windows package
   // redirection making a desktop launch read a different AppData library.
   string directory=args.Length>=2 && args[0]=="--data-dir" ? args[1] : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Library");
   if(!File.Exists(System.IO.Path.Combine(directory,"settings.xml")) && !(args.Length>=2 && args[0]=="--data-dir")) {
    string legacy=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Tiago Music");
    var saved=Storage.Load(legacy); if(saved.Playlists.Count>0) Storage.Save(directory,saved);
   }
   // Prevent two normal sessions from overwriting the same saved library.
   bool created; string key="TiagoMusic_"+System.BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(directory).ToUpperInvariant()))).Replace("-","");
   using(var mutex=new System.Threading.Mutex(true,key,out created)) {
    if(!created) { MessageBox.Show("Tiago Music is already open. Switch to its window on the taskbar.","Tiago Music"); return 0; }
    try { var app=new Application(); var player=new Player(directory); player.Show(); app.Run(); return 0; }
    catch(Exception e) { MessageBox.Show("Tiago Music could not start: "+e.Message,"Tiago Music"); return 1; }
   }
  }
 }
}
