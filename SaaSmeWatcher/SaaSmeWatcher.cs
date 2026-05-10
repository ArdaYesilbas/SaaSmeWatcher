using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Speech.Recognition;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SaaSmeWatcher
{
    public partial class SaaSmeWatcher : Form
    {
        // =========================================================
        // AYARLAR
        // =========================================================

        private const string SAASME_EXE = @"C:\Users\arday\Desktop\YapayZeka SaaSme\YapayZeka.exe";

        private static readonly string[] WAKE_WORDS =
        {
            // Mikrofonu Uyutma / Uyandırma Komutları
            "start listening", "unmute microphone",
            "stop listening", "mute microphone",

            // Açma Komutları
            "wake up", "start", "open", "hello", "system start", "computer start",
            
            // Kapatma Komutları
            "kill yourself", "stop", "close", "exit", "kill", "bye bye", "sleep"
        };

        // =========================================================
        // DEĞİŞKENLER
        // =========================================================
        private SpeechRecognitionEngine _dinleyici;
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private bool _dinliyorMu = false;
        private bool _disposing = false;
        private bool _mikrofonAktif = true;
        private Label _durumLabel;
        private Label _logLabel;
        private string _sonLog = "";

        // =========================================================
        // CONSTRUCTOR
        // =========================================================
        public SaaSmeWatcher()
        {
            InitializeComponent();

            this.Text = "SaaSme Watcher";
            this.Size = new Size(340, 240);
            this.BackColor = Color.FromArgb(15, 23, 42);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            _durumLabel = new Label
            {
                Text = "⏳ Başlatılıyor...",
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(310, 30),
                Location = new Point(10, 15),
                TextAlign = ContentAlignment.MiddleCenter
            };
            this.Controls.Add(_durumLabel);

            _logLabel = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 9f),
                AutoSize = false,
                Size = new Size(310, 80),
                Location = new Point(10, 55),
                TextAlign = ContentAlignment.TopLeft
            };
            this.Controls.Add(_logLabel);

            var wakeTxt = new Label
            {
                Text = "Komutlar:\n" +
                       "🎙️ Mikrofonu Aç/Kapa: \"Start listening\" / \"Stop listening\"\n" +
                       "🟢 Aç: \"Wake up\"  |  🔴 Kapat: \"Kill yourself\"",
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(310, 60),
                Location = new Point(10, 148),
                TextAlign = ContentAlignment.TopLeft
            };
            this.Controls.Add(wakeTxt);

            this.Load += SaaSmeWatcher_Load;
        }

        private void SaaSmeWatcher_Load(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            this.Hide();

            TrayIconKur();
            SesMotorKur();
            BaslangicaEkle();
        }

        private void TrayIconKur()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("📋 Durumu Göster", null, (s, e) => DurumuGoster());
            _trayMenu.Items.Add("🔊 Dinlemeyi Başlat", null, (s, e) => DinlemeyiBaslat());
            _trayMenu.Items.Add("🔇 Dinlemeyi Durdur", null, (s, e) => DinlemeyiDurdur());
            _trayMenu.Items.Add("-");
            _trayMenu.Items.Add("🚀 Uygulamayı Manuel Aç", null, (s, e) => SaaSmeAc());
            _trayMenu.Items.Add("🛑 Uygulamayı Kapat", null, (s, e) => SaaSmeKapat());
            _trayMenu.Items.Add("-");
            _trayMenu.Items.Add("❌ Watcher'ı Kapat", null, (s, e) =>
            {
                _disposing = true;
                Application.Exit();
            });

            _trayIcon = new NotifyIcon
            {
                Text = "SaaSme Watcher — Dinliyor",
                Icon = SystemIcons.Application,
                ContextMenuStrip = _trayMenu,
                Visible = true
            };

            _trayIcon.DoubleClick += (s, e) => DurumuGoster();
        }

        private void DurumuGoster()
        {
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            this.Show();
            this.BringToFront();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
                this.ShowInTaskbar = false;
            }
        }

        private void SesMotorKur()
        {
            try
            {
                var culture = new System.Globalization.CultureInfo("en-US");
                _dinleyici = new SpeechRecognitionEngine(culture);

                var secenekler = new Choices(WAKE_WORDS);
                var gb = new GrammarBuilder(secenekler);
                gb.Culture = culture;

                _dinleyici.LoadGrammar(new Grammar(gb));
                _dinleyici.SetInputToDefaultAudioDevice();

                _dinleyici.SpeechRecognized += OnKelimeTanindi;

                _dinleyici.SpeechRecognitionRejected += (s, ev) =>
                {
                    string duyulan = ev.Result?.Text ?? "Boş";
                    float guv = ev.Result?.Confidence ?? 0f;
                    Log(string.Format("❌ Reddedildi: \"{0}\" (Güven: %{1})", duyulan, (int)(guv * 100)));
                };

                _dinleyici.RecognizeCompleted += (s, ev) =>
                {
                    if (!_disposing && _dinliyorMu)
                        DinlemeyiBaslat();
                };

                DinlemeyiBaslat();
                Log("✅ İngilizce motor ile başlatıldı.");
            }
            catch (Exception ex)
            {
                DurumGuncelle("❌ Ses motoru başlatılamadı.", Color.FromArgb(239, 68, 68));
                Log(string.Format("Kritik hata: {0}", ex.Message));
            }
        }

        private void DinlemeyiBaslat()
        {
            if (_dinleyici == null) return;
            try
            {
                _dinleyici.RecognizeAsync(RecognizeMode.Single);
                _dinliyorMu = true;

                if (_mikrofonAktif)
                {
                    DurumGuncelle("🟢 Dinliyor — Söyle: \"Wake up\"", Color.FromArgb(16, 185, 129));
                    if (_trayIcon != null) _trayIcon.Text = "SaaSme Watcher — 🟢 Dinliyor";
                }
            }
            catch { }
        }

        private void DinlemeyiDurdur()
        {
            if (_dinleyici == null) return;
            try
            {
                _dinleyici.RecognizeAsyncStop();
                _dinliyorMu = false;
                DurumGuncelle("🔴 Dinleme durduruldu", Color.FromArgb(239, 68, 68));
            }
            catch { }
        }

        private void OnKelimeTanindi(object sender, SpeechRecognizedEventArgs e)
        {
            string tanınan = e.Result.Text.ToLower();
            float guven = e.Result.Confidence;

            Log(string.Format("✅ Algılandı: \"{0}\" (Güven: %{1})", tanınan, (int)(guven * 100)));

            if (guven >= 0.60f)
            {
                // 1. MİKROFON KONTROLÜ
                if (tanınan.Contains("stop listening") || tanınan.Contains("mute"))
                {
                    _mikrofonAktif = false;
                    Log("🔇 Mikrofon UYUTULDU. Sadece 'Start listening' komutuna tepki verecek.");
                    DurumGuncelle("🔇 Uyuyor — 'Start listening' deyin", Color.Orange);
                    return;
                }

                if (tanınan.Contains("start listening") || tanınan.Contains("unmute"))
                {
                    _mikrofonAktif = true;
                    Log("🔊 Mikrofon UYANDIRILDI! Uygulama açılıyor...");
                    DurumGuncelle("🟢 Dinliyor — Söyle: \"Wake up\"", Color.FromArgb(16, 185, 129));
                    SaaSmeAc();
                    return;
                }

                // 2. MİKROFON KAPALIYSA YOK SAY
                if (!_mikrofonAktif)
                {
                    Log(string.Format("ℹ️ Mikrofon uykuda. '{0}' komutu yoksayıldı.", tanınan));
                    return;
                }

                // 3. NORMAL KOMUTLAR
                if (tanınan.Contains("kill") || tanınan.Contains("close") || tanınan.Contains("stop") || tanınan.Contains("exit") || tanınan.Contains("bye") || tanınan.Contains("sleep"))
                {
                    SaaSmeKapat();
                }
                else if (tanınan.Contains("wake up") || tanınan.Contains("start") || tanınan.Contains("open") || tanınan.Contains("hello"))
                {
                    SaaSmeAc();
                }
            }
            else
            {
                Log(string.Format("⚠️ Güven yetersiz (%{0}), yoksayıldı.", (int)(guven * 100)));
            }
        }

        private void OllamaAc()
        {
            try
            {
                if (Process.GetProcessesByName("ollama").Length > 0 ||
                    Process.GetProcessesByName("ollama app").Length > 0)
                {
                    Log("ℹ️ Ollama zaten aktif.");
                    return;
                }

                Log("🦙 Ollama başlatılıyor...");

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string ollamaExeYolu = Path.Combine(localAppData, @"Programs\Ollama\ollama app.exe");

                if (File.Exists(ollamaExeYolu))
                {
                    Process.Start(new ProcessStartInfo(ollamaExeYolu)
                    {
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Minimized
                    });

                    Task.Run(async delegate
                    {
                        for (int i = 0; i < 30; i++)
                        {
                            await Task.Delay(500);

                            var procs = Process.GetProcesses();
                            bool mudehaleEdildi = false;

                            foreach (var p in procs)
                            {
                                if (p.ProcessName.ToLower().Contains("ollama") && p.MainWindowHandle != IntPtr.Zero)
                                {
                                    p.CloseMainWindow();
                                    ShowWindow(p.MainWindowHandle, 0);
                                    mudehaleEdildi = true;
                                }
                            }
                            if (mudehaleEdildi) return;
                        }
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo("ollama", "serve")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                    Log("⚠️ Ollama App bulunamadı, arka planda başlatıldı.");
                }
            }
            catch (Exception ex)
            {
                Log(string.Format("⚠️ Ollama başlatılamadı: {0}", ex.Message));
            }
        }

        private void OllamaKapat()
        {
            try
            {
                bool kapatildi = false;
                var procs = Process.GetProcesses();

                foreach (var p in procs)
                {
                    if (p.ProcessName.ToLower().Contains("ollama"))
                    {
                        p.Kill();
                        kapatildi = true;
                    }
                }

                if (kapatildi)
                {
                    Log("🦙 Ollama durduruldu.");
                }
            }
            catch { }
        }

        private void SaaSmeAc()
        {
            try
            {
                OllamaAc();

                string exeName = Path.GetFileNameWithoutExtension(SAASME_EXE);
                var prosesler = Process.GetProcessesByName(exeName);
                if (prosesler.Length > 0)
                {
                    Log("ℹ️ Uygulama zaten açık, öne getiriliyor...");
                    foreach (var p in prosesler)
                    {
                        if (p.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(p.MainWindowHandle, 9);
                            SetForegroundWindow(p.MainWindowHandle);
                        }
                    }
                    return;
                }

                if (!File.Exists(SAASME_EXE))
                {
                    Log(string.Format("❌ Dosya bulunamadı: {0}", SAASME_EXE));
                    return;
                }

                Process.Start(new ProcessStartInfo(SAASME_EXE)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(SAASME_EXE)
                });

                Log("🚀 Uygulama başlatıldı!");
                _trayIcon.ShowBalloonTip(2000, "SaaSme Watcher", "Uygulama ve Ollama açılıyor...", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Log(string.Format("❌ Açma hatası: {0}", ex.Message));
            }
        }

        private void SaaSmeKapat()
        {
            try
            {
                OllamaKapat();

                string exeName = Path.GetFileNameWithoutExtension(SAASME_EXE);
                var prosesler = Process.GetProcessesByName(exeName);

                if (prosesler.Length > 0)
                {
                    foreach (var p in prosesler)
                    {
                        p.Kill();
                    }
                    Log("🛑 Uygulama kendini imha etti!");
                    _trayIcon.ShowBalloonTip(2000, "SaaSme Watcher", "Uygulama ve Ollama kapatıldı.", ToolTipIcon.Warning);
                }
                else
                {
                    Log("ℹ️ Uygulama zaten kapalı durumda.");
                }
            }
            catch (Exception ex)
            {
                Log(string.Format("❌ Kapatma hatası: {0}", ex.Message));
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private void BaslangicaEkle()
        {
            try
            {
                string uygulamaYolu = Application.ExecutablePath;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null) key.SetValue("SaaSmeWatcher", string.Format("\"{0}\"", uygulamaYolu));
                }
            }
            catch { }
        }

        private void DurumGuncelle(string mesaj, Color renk)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired) { this.Invoke((MethodInvoker)(() => DurumGuncelle(mesaj, renk))); return; }
            _durumLabel.Text = mesaj;
            _durumLabel.ForeColor = renk;
        }

        private void Log(string mesaj)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired) { this.Invoke((MethodInvoker)(() => Log(mesaj))); return; }
            _sonLog = string.Format("[{0:HH:mm:ss}] {1}\n", DateTime.Now, mesaj) + _sonLog;
            if (_sonLog.Length > 500) _sonLog = _sonLog.Substring(0, 500);
            _logLabel.Text = _sonLog;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _disposing = true;
            if (_trayIcon != null) _trayIcon.Visible = false;
            try { if (_dinleyici != null) _dinleyici.RecognizeAsyncCancel(); } catch { }
            if (_dinleyici != null) _dinleyici.Dispose();
            base.OnFormClosing(e);
        }
    }
}