using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;

namespace DbManager
{
    public partial class Form1 : DevExpress.XtraEditors.XtraForm
    {
        // ── Dosya yolları ──────────────────────────────────────────────────────
        private static readonly string FavoritesFile = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "favorites.txt");
        private static readonly string ServersFile = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "servers.txt");
        private static readonly string SettingsFile = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "dbmanager.ini");

        // ── Ayar değerleri ─────────────────────────────────────────────────────
        private int ayarBackupUyariGun = 1;
        private int ayarBoyutUyariMb   = 50000;
        private string ayarBackupKlasor = "";

        // ── Durum ──────────────────────────────────────────────────────────────
        private DatabaseHelper dbHelper;
        private HashSet<string> favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<string> savedServers  = new List<string>();
        private string currentConnectionString;
        private string secilenDb;
        private DataTable mainData;

        // ── Constructor ────────────────────────────────────────────────────────
        private const string DefaultConnectionString =
            "Server=127.0.0.1,1433;Database=master;User Id=sa;Password=789.Asdf;" +
            "MultipleActiveResultSets=true;TrustServerCertificate=True;";

        public Form1()
        {
            InitializeComponent();
            currentConnectionString = DefaultConnectionString;
            dbHelper = new DatabaseHelper(currentConnectionString);
        }

        // ── Form Load ──────────────────────────────────────────────────────────
        private void Form1_Load(object sender, EventArgs e)
        {
            LoadFavorites();
            LoadServers();
            LoadAppSettings();
            ConfigureGrids();
            LoadDatabases();
        }

        // ══════════════════════════════════════════════════════════════════════
        // GRID YAPILANDIRMASI
        // ══════════════════════════════════════════════════════════════════════

        private void ConfigureGrids()
        {
            GridHelper.GridViewSekillendir(gridView1);
            gridView1.OptionsView.ShowFooter = true;

            GridHelper.GridViewSekillendir(gridViewJobs);
            GridHelper.GridViewSekillendir(gridViewTables);
            GridHelper.GridViewSekillendir(gridViewProcs);
            GridHelper.GridViewSekillendir(gridViewFiles);
            GridHelper.GridViewSekillendir(gridViewCompare);
            GridHelper.GridViewSekillendir(gridViewBaglantilar);
            GridHelper.GridViewSekillendir(gridViewSorgular);
            GridHelper.GridViewSekillendir(gridViewKilitler);
            GridHelper.GridViewSekillendir(gridViewTempDb);
            GridHelper.GridViewSekillendir(gridViewKullanicilar);
        }

        private void ConfigureDbColumns()
        {
            if (gridView1.Columns.Count == 0) return;

            SetCol(gridView1, "NAME",             "Veritabanı Adı", 200, true);
            SetCol(gridView1, "DURUM",            "Durum",           90, true);
            SetCol(gridView1, "BOYUT_MB",         "Boyut (MB)",     160, true);
            SetCol(gridView1, "DATA_MB",          "Data (MB)",       90, true);
            SetCol(gridView1, "LOG_MB",           "Log (MB)",        85, true);
            SetCol(gridView1, "RECOVERY_MODEL",   "Recovery",       100, true);
            SetCol(gridView1, "OLUSTURMA_TARIHI", "Oluşturulma",    130, true);
            SetCol(gridView1, "SON_BACKUP",       "Son Backup",     130, true);
            SetCol(gridView1, "SALT_OKUNUR",      "Salt Okunur",     85, true);
            SetCol(gridView1, "SAYFA_DOGRULAMA",  "Sayfa Doğrulama",105, true);
            SetCol(gridView1, "DB_ID",            "",                 0, false);
            SetCol(gridView1, "DURUM_KOD",        "",                 0, false);

            foreach (string col in new[] { "BOYUT_MB", "DATA_MB", "LOG_MB" })
                if (gridView1.Columns[col] != null)
                    gridView1.Columns[col].AppearanceCell.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Far;

            if (gridView1.Columns["NAME"] != null)
            {
                gridView1.Columns["NAME"].Summary.Clear();
                gridView1.Columns["NAME"].Summary.Add(DevExpress.Data.SummaryItemType.Count, "NAME", "Adet: {0}");
            }
            if (gridView1.Columns["BOYUT_MB"] != null)
            {
                gridView1.Columns["BOYUT_MB"].Summary.Clear();
                gridView1.Columns["BOYUT_MB"].Summary.Add(DevExpress.Data.SummaryItemType.Sum, "BOYUT_MB", "Toplam: {0:N0} MB");
            }
        }

        private void ConfigureJobColumns()
        {
            if (gridViewJobs.Columns.Count == 0) return;
            SetCol(gridViewJobs, "JOB_ADI",     "Job Adı",              250, true);
            SetCol(gridViewJobs, "DURUM",        "Durum",                 80, true);
            SetCol(gridViewJobs, "SON_DURUM",    "Son Çalışma Durumu",   145, true);
            SetCol(gridViewJobs, "SON_CALISMA",  "Son Çalışma",          130, true);
            SetCol(gridViewJobs, "KATEGORI",     "Kategori",             120, true);
            SetCol(gridViewJobs, "ACIKLAMA",     "Açıklama",             200, true);
            SetCol(gridViewJobs, "ENABLED_BIT",  "",                       0, false);
        }

        private static void SetCol(GridView view, string fieldName, string caption, int width, bool visible = true)
        {
            var col = view.Columns[fieldName];
            if (col == null) return;
            col.Caption = caption;
            if (visible && width > 0) col.Width = width;
            col.Visible = visible;
            if (!visible) col.VisibleIndex = -1;
        }

        // ══════════════════════════════════════════════════════════════════════
        // VERİ YÜKLEME
        // ══════════════════════════════════════════════════════════════════════

        private void ClearDetailGrids()
        {
            secilenDb = null;
            gridControlTables.DataSource = null;
            gridControlProcs.DataSource  = null;
            gridControlFiles.DataSource  = null;
        }

        private void LoadDatabases()
        {
            SetStatus("Yükleniyor...");
            ClearDetailGrids();
            try
            {
                mainData = dbHelper.GetDatabaseDetails();
                gridControl1.DataSource = null;
                gridControl1.DataSource = mainData;
                ConfigureDbColumns();
                UpdateSummaryCards(mainData);
                UpdateServerLabel();
                CheckWarnings(mainData);
                SetStatus($"{mainData?.Rows.Count ?? 0} veritabanı listelendi.");
            }
            catch (Exception ex)
            {
                SetStatus("Hata: " + ex.Message);
                XtraMessageBox.Show("Veritabanları yüklenirken hata:\n\n" + ex.Message,
                    "Bağlantı Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadJobs()
        {
            SetStatus("Jobs yükleniyor...");
            try
            {
                var jobs = dbHelper.GetSqlJobs();
                gridControlJobs.DataSource = null;
                gridControlJobs.DataSource = jobs;
                ConfigureJobColumns();
                SetStatus($"{jobs?.Rows.Count ?? 0} SQL Agent Job listelendi.");
            }
            catch (Exception ex)
            {
                SetStatus("Jobs yüklenirken hata: " + ex.Message);
            }
        }

        private void LoadDetailTab()
        {
            if (string.IsNullOrEmpty(secilenDb)) return;
            try
            {
                switch (tabDetail.SelectedIndex)
                {
                    case 0:
                        var tables = dbHelper.GetDatabaseTables(secilenDb);
                        gridControlTables.DataSource = null;
                        gridControlTables.DataSource = tables;
                        break;
                    case 1:
                        var procs = dbHelper.GetDatabaseProcedures(secilenDb);
                        gridControlProcs.DataSource = null;
                        gridControlProcs.DataSource = procs;
                        break;
                    case 2:
                        var files = dbHelper.GetDatabaseFiles(secilenDb);
                        gridControlFiles.DataSource = null;
                        gridControlFiles.DataSource = files;
                        break;
                }
            }
            catch (Exception ex)
            {
                SetStatus($"[{secilenDb}] detay yüklenirken hata: " + ex.Message);
            }
        }

        private void UpdateSummaryCards(DataTable dt)
        {
            if (dt == null) return;
            int total   = dt.Rows.Count;
            int online  = dt.AsEnumerable().Count(r => r["DURUM_KOD"].ToString() == "ONLINE");
            int offline = total - online;
            double totalMb = dt.AsEnumerable().Sum(r =>
                r["BOYUT_MB"] == DBNull.Value ? 0 : Convert.ToDouble(r["BOYUT_MB"]));
            double totalGb = Math.Round(totalMb / 1024, 1);

            DateTime? oldestBackup = null;
            foreach (DataRow row in dt.Rows)
            {
                if (row["SON_BACKUP"] != DBNull.Value)
                {
                    var d = Convert.ToDateTime(row["SON_BACKUP"]);
                    if (oldestBackup == null || d < oldestBackup) oldestBackup = d;
                }
            }

            lblCardTotalVal.Text   = total.ToString();
            lblCardOnlineVal.Text  = online.ToString();
            lblCardOfflineVal.Text = offline.ToString();
            lblCardSizeVal.Text    = totalGb >= 1 ? $"{totalGb:N1} GB" : $"{totalMb:N0} MB";
            lblCardBackupVal.Text  = oldestBackup.HasValue
                ? oldestBackup.Value.ToString("dd.MM.yy")
                : "Yok";

            if (offline > 0)
                lblCardOfflineVal.Appearance.ForeColor = Color.FromArgb(231, 76, 60);
        }

        private void UpdateServerLabel()
        {
            try
            {
                var info = dbHelper.GetServerInfo();
                if (info == null || info.Rows.Count == 0) return;
                var row = info.Rows[0];
                string srv = row["SUNUCU_ADI"]?.ToString() ?? "—";
                string ed  = row["EDITION"]?.ToString() ?? "—";
                string ver = row["VERSIYON"]?.ToString() ?? "—";
                lblServerName.Text = $"  {srv}  |  {ed}  |  v{ver}";
                this.Text = $"DB Manager — {srv}";
                lblAppSub.Text = srv;
            }
            catch { }
        }

        // ══════════════════════════════════════════════════════════════════════
        // NAV BUTTON EVENTS
        // ══════════════════════════════════════════════════════════════════════

        private void btnNavDBs_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabPageDBs;
            gridControl1.DataSource = mainData;
            SetStatus("Tüm veritabanları listeleniyor.");
        }

        private void btnNavJobs_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabPageJobs;
            LoadJobs();
        }

        private void btnNavFavoriler_Click(object sender, EventArgs e)
        {
            ShowFavorites();
        }

        private void btnNavSunucular_Click(object sender, EventArgs e)
        {
            ShowServerSwitcher();
        }

        private void btnNavFragmentasyon_Click(object sender, EventArgs e)
        {
            ClearDetailGrids();
            tabMain.SelectedTab = tabPageFrag;
            string db = ResolveDb();
            if (!string.IsNullOrEmpty(db))
                LoadFragmentasyon(db);
            else
                SetStatus("Fragmentasyon: veritabanı listesi henüz yüklenmedi.");
        }

        private void btnNavShrink_Click(object sender, EventArgs e)
        {
            string db = gridView1.GetFocusedRowCellValue("NAME")?.ToString();
            if (!string.IsNullOrEmpty(db)) secilenDb = db;
            ShowShrinkDialog();
        }

        // ══════════════════════════════════════════════════════════════════════
        // TOOLBAR EVENTS
        // ══════════════════════════════════════════════════════════════════════

        private void btnYenile_Click(object sender, EventArgs e) => LoadDatabases();

        private void btnExcel_Click(object sender, EventArgs e)
        {
            if (tabMain.SelectedTab == tabPageDBs)
                GridHelper.ExportExcel(gridView1, "CadSql Database Listesi");
            else if (tabMain.SelectedTab == tabPageJobs)
                GridHelper.ExportExcel(gridViewJobs, "SQL Agent Jobs");
        }

        private void btnBackup_Click(object sender, EventArgs e)
        {
            string dbName = gridView1.GetFocusedRowCellValue("NAME")?.ToString();
            if (string.IsNullOrEmpty(dbName))
            {
                XtraMessageBox.Show("Lütfen önce bir veritabanı seçin.", "Uyarı",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DoBackup(dbName);
        }

        private void btnSunucuBilgisi_Click(object sender, EventArgs e)
        {
            try
            {
                var info = dbHelper.GetServerInfo();
                if (info == null || info.Rows.Count == 0) return;
                var row = info.Rows[0];
                string msg = $"Sunucu Adı : {row["SUNUCU_ADI"]}\n" +
                             $"Edition    : {row["EDITION"]}\n" +
                             $"Versiyon   : {row["VERSIYON"]}\n" +
                             $"Karşılaştırma: {row["KARSILASTIRMA"]}\n\n" +
                             $"Toplam DB  : {row["TOPLAM_DB"]}\n" +
                             $"Online     : {row["ONLINE_DB"]}\n" +
                             $"Offline    : {row["OFFLINE_DB"]}\n" +
                             $"Toplam Disk: {row["TOPLAM_GB"]} GB";
                XtraMessageBox.Show(msg, "Sunucu Bilgisi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show("Sunucu bilgisi alınamadı:\n" + ex.Message, "Hata",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnFavoriler_Click(object sender, EventArgs e) => ShowFavorites();

        private void btnBoyutaSirala_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabPageDBs;
            gridControl1.DataSource = mainData;
            if (gridView1.Columns["BOYUT_MB"] != null)
            {
                gridView1.ClearSorting();
                gridView1.Columns["BOYUT_MB"].SortOrder = DevExpress.Data.ColumnSortOrder.Descending;
            }
        }

        private void btnRiskli_Click(object sender, EventArgs e)
        {
            if (mainData == null) return;
            var rows = mainData.AsEnumerable()
                .Where(r => r["SON_BACKUP"] == DBNull.Value ||
                            (DateTime.Now - Convert.ToDateTime(r["SON_BACKUP"])).TotalDays > 1)
                .ToList();
            if (rows.Count == 0)
            {
                SetStatus("Tüm veritabanlarında güncel backup mevcut.");
                return;
            }
            tabMain.SelectedTab = tabPageDBs;
            gridControl1.DataSource = rows.CopyToDataTable();
            SetStatus($"{rows.Count} riskli veritabanı gösteriliyor.");
        }

        // ══════════════════════════════════════════════════════════════════════
        // FAVORİLER
        // ══════════════════════════════════════════════════════════════════════

        private void LoadFavorites()
        {
            if (File.Exists(FavoritesFile))
                foreach (string line in File.ReadAllLines(FavoritesFile))
                    if (!string.IsNullOrWhiteSpace(line)) favorites.Add(line.Trim());
        }

        private void SaveFavorites() => File.WriteAllLines(FavoritesFile, favorites);

        private void ShowFavorites()
        {
            if (mainData == null) return;
            tabMain.SelectedTab = tabPageDBs;
            if (favorites.Count == 0)
            {
                gridControl1.DataSource = mainData;
                SetStatus("Favori yok. Tüm liste gösteriliyor.");
                return;
            }
            var rows = mainData.AsEnumerable()
                .Where(r => favorites.Contains(r["NAME"].ToString()))
                .ToList();
            if (rows.Count > 0)
                gridControl1.DataSource = rows.CopyToDataTable();
            SetStatus($"{rows.Count} favori veritabanı gösteriliyor.");
        }

        private void ToggleFavorite(string dbName)
        {
            if (string.IsNullOrEmpty(dbName)) return;
            if (favorites.Contains(dbName))
            {
                favorites.Remove(dbName);
                SetStatus($"'{dbName}' favorilerden çıkarıldı.");
            }
            else
            {
                favorites.Add(dbName);
                SetStatus($"'{dbName}' favorilere eklendi.");
            }
            SaveFavorites();
            gridControl1.Refresh();
        }

        // ══════════════════════════════════════════════════════════════════════
        // SUNUCU DEĞİŞTİRME
        // ══════════════════════════════════════════════════════════════════════

        private void LoadServers()
        {
            savedServers.Clear();
            savedServers.Add(currentConnectionString);
            if (File.Exists(ServersFile))
                foreach (string line in File.ReadAllLines(ServersFile))
                    if (!string.IsNullOrWhiteSpace(line) && !savedServers.Contains(line.Trim()))
                        savedServers.Add(line.Trim());
        }

        private void ShowServerSwitcher()
        {
            var form = new XtraForm
            {
                Text = "Sunucu Değiştir",
                Width = 620,
                Height = 195,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
            };

            var lblConn   = new LabelControl { Location = new Point(12, 14), AutoSizeMode = LabelAutoSizeMode.Horizontal, Text = "Connection String:" };
            var txt       = new TextEdit     { Location = new Point(12, 34), Size = new Size(580, 24), Text = currentConnectionString };
            var btnTest   = new SimpleButton { Text = "Bağlantıyı Test Et", Location = new Point(12, 72), Width = 145, Height = 28 };
            var lblStatus = new LabelControl { Location = new Point(164, 78), AutoSizeMode = LabelAutoSizeMode.Horizontal };
            var btnBaglan = new SimpleButton { Text = "Bağlan",  Location = new Point(12, 112), Width = 110, Height = 30 };
            var btnIptal  = new SimpleButton { Text = "İptal",   Location = new Point(130, 112), Width = 80,  Height = 30 };

            btnTest.Click += (s, ev) =>
            {
                var tmp = new DatabaseHelper(txt.Text);
                bool ok = tmp.TestConnection();
                lblStatus.Text = ok ? "Başarılı" : "Bağlantı kurulamadı";
                lblStatus.Appearance.ForeColor = ok ? Color.FromArgb(39, 174, 96) : Color.FromArgb(231, 76, 60);
            };

            btnBaglan.Click += (s, ev) =>
            {
                currentConnectionString = txt.Text;
                dbHelper.SetConnectionString(currentConnectionString);
                if (!savedServers.Contains(currentConnectionString))
                {
                    savedServers.Add(currentConnectionString);
                    File.AppendAllText(ServersFile, currentConnectionString + Environment.NewLine);
                }
                form.Close();
                LoadDatabases();
            };

            btnIptal.Click += (s, ev) => form.Close();
            form.Controls.AddRange(new Control[] { lblConn, txt, btnTest, lblStatus, btnBaglan, btnIptal });
            form.ShowDialog(this);
        }

        // ══════════════════════════════════════════════════════════════════════
        // KARŞILAŞTIRMA
        // ══════════════════════════════════════════════════════════════════════

        private void btnCompareRun_Click(object sender, EventArgs e)
        {
            string conn2 = txtCompareConn.Text?.Trim();
            if (string.IsNullOrEmpty(conn2))
            {
                lblCompareStatus.Text = "Connection string gerekli.";
                return;
            }
            try
            {
                lblCompareStatus.Text = "Karşılaştırılıyor...";
                lblCompareStatus.Refresh();

                var helper2 = new DatabaseHelper(conn2);
                if (!helper2.TestConnection())
                {
                    lblCompareStatus.Text = "İkinci sunucuya bağlanılamadı.";
                    lblCompareStatus.Appearance.ForeColor = Color.FromArgb(231, 76, 60);
                    return;
                }

                var list1  = dbHelper.GetDatabaseDetails();
                var list2  = helper2.GetDatabaseDetails();
                var names1 = new HashSet<string>(list1.AsEnumerable().Select(r => r["NAME"].ToString()), StringComparer.OrdinalIgnoreCase);
                var names2 = new HashSet<string>(list2.AsEnumerable().Select(r => r["NAME"].ToString()), StringComparer.OrdinalIgnoreCase);

                var ct = new DataTable();
                ct.Columns.Add("VERITABANI",     typeof(string));
                ct.Columns.Add("DURUM",          typeof(string));
                ct.Columns.Add("SUNUCU1_MB",     typeof(string));
                ct.Columns.Add("SUNUCU2_MB",     typeof(string));

                foreach (string name in names1.Union(names2).OrderBy(n => n))
                {
                    bool inS1 = names1.Contains(name), inS2 = names2.Contains(name);
                    string durum = inS1 && inS2 ? "Her İkisinde"
                                 : inS1 ? "Sadece Sunucu 1"
                                 : "Sadece Sunucu 2";
                    string b1 = GetMb(list1, name), b2 = GetMb(list2, name);
                    ct.Rows.Add(name, durum, b1, b2);
                }

                gridControlCompare.DataSource = null;
                gridControlCompare.DataSource = ct;
                GridHelper.GridViewSekillendir(gridViewCompare);

                int fark = names1.Except(names2, StringComparer.OrdinalIgnoreCase).Count() +
                           names2.Except(names1, StringComparer.OrdinalIgnoreCase).Count();
                lblCompareStatus.Text = $"Toplam {ct.Rows.Count} DB — {fark} farklılık";
                lblCompareStatus.Appearance.ForeColor = fark > 0
                    ? Color.FromArgb(243, 156, 18)
                    : Color.FromArgb(39, 174, 96);
            }
            catch (Exception ex)
            {
                lblCompareStatus.Text = "Hata: " + ex.Message;
                lblCompareStatus.Appearance.ForeColor = Color.FromArgb(231, 76, 60);
            }
        }

        private static string GetMb(DataTable dt, string name)
        {
            var row = dt.AsEnumerable().FirstOrDefault(r =>
                r["NAME"].ToString().Equals(name, StringComparison.OrdinalIgnoreCase));
            if (row == null || row["BOYUT_MB"] == DBNull.Value) return "—";
            return row["BOYUT_MB"] + " MB";
        }

        // ══════════════════════════════════════════════════════════════════════
        // BACKUP
        // ══════════════════════════════════════════════════════════════════════

        private void DoBackup(string dbName)
        {
            var dlg = new FolderBrowserDialog
            {
                Description = $"'{dbName}' için backup konumu seçin",
                ShowNewFolderButton = true,
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            string fileName = $"{dbName}_{DateTime.Now:yyyyMMdd_HHmm}.bak";
            string fullPath = Path.Combine(dlg.SelectedPath, fileName);

            SetStatus($"'{dbName}' backup alınıyor...");
            try
            {
                dbHelper.BackupDatabase(dbName, fullPath);
                SetStatus($"Backup tamamlandı: {fullPath}");
                XtraMessageBox.Show($"Backup başarıyla alındı:\n{fullPath}", "Başarılı",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                SetStatus("Backup hatası: " + ex.Message);
                XtraMessageBox.Show($"Backup alınırken hata:\n{ex.Message}", "Hata",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // GRID EVENTS
        // ══════════════════════════════════════════════════════════════════════

        private void gridView1_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
        {
            secilenDb = gridView1.GetFocusedRowCellValue("NAME")?.ToString();
            if (!string.IsNullOrEmpty(secilenDb))
                LoadDetailTab();
        }

        private void gridView1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            var hitInfo = gridView1.CalcHitInfo(e.Location);
            if (!hitInfo.InRow) return;
            gridView1.FocusedRowHandle = hitInfo.RowHandle;
            secilenDb = gridView1.GetFocusedRowCellValue("NAME")?.ToString();
            cmsFavori.Text = favorites.Contains(secilenDb ?? "")
                ? "Favorilerden Çıkar"
                : "Favorilere Ekle";
            contextMenuStrip1.Show(gridControl1, e.Location);
        }

        private void gridView1_CustomDrawCell(object sender, DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e)
        {
            if (e.Column.FieldName == "DURUM")
            {
                string val = e.CellValue?.ToString() ?? "";
                if (val == "Çevrimiçi")       e.Appearance.ForeColor = Color.FromArgb(39, 174, 96);
                else if (val == "Çevrimdışı") e.Appearance.ForeColor = Color.FromArgb(231, 76, 60);
                else                           e.Appearance.ForeColor = Color.FromArgb(243, 156, 18);
            }
            else if (e.Column.FieldName == "SON_BACKUP")
            {
                if (e.CellValue == null || e.CellValue == DBNull.Value)
                {
                    e.Appearance.ForeColor = Color.FromArgb(231, 76, 60);
                    // DisplayText is read-only; use AppearanceCell font for emphasis
                }
                else
                {
                    int gun = (int)(DateTime.Now - Convert.ToDateTime(e.CellValue)).TotalDays;
                    if (gun > 7)     e.Appearance.ForeColor = Color.FromArgb(231, 76, 60);
                    else if (gun > 0) e.Appearance.ForeColor = Color.FromArgb(243, 156, 18);
                    else              e.Appearance.ForeColor = Color.FromArgb(39, 174, 96);
                }
            }
        }

        private void gridView1_CustomDrawRowIndicator(object sender, RowIndicatorCustomDrawEventArgs e)
        {
            if (!e.Info.IsRowIndicator || e.RowHandle < 0) return;
            string dbName = gridView1.GetRowCellValue(e.RowHandle, "NAME")?.ToString();
            if (!string.IsNullOrEmpty(dbName) && favorites.Contains(dbName))
            {
                e.Appearance.BackColor = Color.FromArgb(155, 89, 182);
                e.Appearance.ForeColor = Color.White;
            }
        }

        private void gridViewJobs_CustomDrawCell(object sender, DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e)
        {
            if (e.Column.FieldName != "SON_DURUM") return;
            string val = e.CellValue?.ToString() ?? "";
            if (val == "Başarılı")          e.Appearance.ForeColor = Color.FromArgb(39, 174, 96);
            else if (val == "Başarısız")    e.Appearance.ForeColor = Color.FromArgb(231, 76, 60);
            else if (val == "Devam Ediyor") e.Appearance.ForeColor = Color.FromArgb(243, 156, 18);
        }

        private void gridViewCompare_CustomDrawCell(object sender, DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e)
        {
            if (e.Column.FieldName != "DURUM") return;
            string val = e.CellValue?.ToString() ?? "";
            if (val == "Her İkisinde")       e.Appearance.ForeColor = Color.FromArgb(39, 174, 96);
            else if (val == "Sadece Sunucu 1") e.Appearance.ForeColor = Color.FromArgb(243, 156, 18);
            else if (val == "Sadece Sunucu 2") e.Appearance.ForeColor = Color.FromArgb(0, 174, 219);
        }

        private void tabDetail_SelectedIndexChanged(object sender, EventArgs e) => LoadDetailTab();

        private void tabMain_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool dbTab = tabMain.SelectedTab == tabPageDBs;
            pnlDetail.Visible      = dbTab;
            splitterDetail.Visible = dbTab;

            if      (tabMain.SelectedTab == tabPageJobs)         LoadJobs();
            else if (tabMain.SelectedTab == tabPageBaglantilar)  LoadActiveConnections();
            else if (tabMain.SelectedTab == tabPageSorgular)     LoadLongRunningQueries();
            else if (tabMain.SelectedTab == tabPageKilitler)     LoadLocks();
            else if (tabMain.SelectedTab == tabPageTempDb)       LoadTempDb();
            else if (tabMain.SelectedTab == tabPageKullanicilar) LoadDatabaseUsers();
            else if (tabMain.SelectedTab == tabPageAyarlar)      LoadSettingsToUI();
        }

        // ══════════════════════════════════════════════════════════════════════
        // CONTEXT MENU
        // ══════════════════════════════════════════════════════════════════════

        private void cmsYenile_Click(object sender, EventArgs e) => LoadDatabases();

        private void cmsFavori_Click(object sender, EventArgs e) => ToggleFavorite(secilenDb);

        private void cmsBackup_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(secilenDb)) DoBackup(secilenDb);
        }

        private void cmsAdKopyala_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(secilenDb))
            {
                Clipboard.SetText(secilenDb);
                SetStatus($"'{secilenDb}' panoya kopyalandı.");
            }
        }

        private void cmsScript_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(secilenDb)) return;
            tabDetail.SelectedIndex = 0;
            LoadDetailTab();
        }

        private void cmsFragmentasyon_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(secilenDb)) return;
            tabMain.SelectedTab = tabPageFrag;
            LoadFragmentasyon(secilenDb);
        }

        // ══════════════════════════════════════════════════════════════════════
        // FRAGMENTASYON
        // ══════════════════════════════════════════════════════════════════════

        private DataTable fragData;

        private void LoadFragmentasyon(string dbName)
        {
            if (string.IsNullOrEmpty(dbName)) return;
            lblFragDbName.Text = $"Analiz ediliyor: {dbName}  —  lütfen bekleyin...";
            SetFragBusy(true);
            SetStatus($"'{dbName}' fragmentasyon analizi yapılıyor...");

            System.Threading.Tasks.Task.Run(() => dbHelper.GetFragmentation(dbName))
                .ContinueWith(t =>
                {
                    Invoke(new Action(() =>
                    {
                        SetFragBusy(false);
                        if (t.Exception != null)
                        {
                            SetStatus("Fragmentasyon hatası: " + t.Exception.InnerException?.Message);
                            lblFragDbName.Text = $"Hata: {t.Exception.InnerException?.Message}";
                            return;
                        }
                        fragData = t.Result;
                        gridControlFrag.DataSource = null;
                        gridControlFrag.DataSource = fragData;
                        ConfigureFragColumns();
                        UpdateFragCards(fragData);
                        lblFragDbName.Text = $"Veritabanı: {dbName}   |   {fragData?.Rows.Count ?? 0} index analiz edildi";
                        SetStatus($"Fragmentasyon analizi tamamlandı: {fragData?.Rows.Count ?? 0} index.");
                    }));
                });
        }

        private void ConfigureFragColumns()
        {
            GridHelper.GridViewSekillendir(gridViewFrag);
            gridViewFrag.OptionsView.ShowFooter = true;
            if (gridViewFrag.Columns.Count == 0) return;

            SetCol(gridViewFrag, "TABLO_ADI",    "Tablo",            200);
            SetCol(gridViewFrag, "INDEX_ADI",    "Index Adı",         220);
            SetCol(gridViewFrag, "INDEX_TIPI",   "Tip",                90);
            SetCol(gridViewFrag, "FRAGMENTASYON","Fragmentasyon %",   130);
            SetCol(gridViewFrag, "DURUM",        "Durum",              80);
            SetCol(gridViewFrag, "SAYFA_SAYISI", "Sayfa",              80);
            SetCol(gridViewFrag, "KAYIT_SAYISI", "Kayıt",              90);

            foreach (string c in new[] { "FRAGMENTASYON", "SAYFA_SAYISI", "KAYIT_SAYISI" })
                if (gridViewFrag.Columns[c] != null)
                    gridViewFrag.Columns[c].AppearanceCell.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Far;

            if (gridViewFrag.Columns["FRAGMENTASYON"] != null)
            {
                gridViewFrag.Columns["FRAGMENTASYON"].DisplayFormat.FormatString = "{0:N1} %";
                gridViewFrag.Columns["FRAGMENTASYON"].Summary.Clear();
                gridViewFrag.Columns["FRAGMENTASYON"].Summary.Add(DevExpress.Data.SummaryItemType.Average, "FRAGMENTASYON", "Ort: {0:N1} %");
            }
            if (gridViewFrag.Columns["TABLO_ADI"] != null)
            {
                gridViewFrag.Columns["TABLO_ADI"].Summary.Clear();
                gridViewFrag.Columns["TABLO_ADI"].Summary.Add(DevExpress.Data.SummaryItemType.Count, "TABLO_ADI", "Adet: {0}");
            }
        }

        private void UpdateFragCards(DataTable dt)
        {
            if (dt == null) return;
            int total  = dt.Rows.Count;
            int kritik = dt.AsEnumerable().Count(r => r["DURUM"].ToString() == "Kritik");
            int orta   = dt.AsEnumerable().Count(r => r["DURUM"].ToString() == "Orta");
            int iyi    = dt.AsEnumerable().Count(r => r["DURUM"].ToString() == "İyi");
            lblFragTotalVal.Text  = total.ToString();
            lblFragKritikVal.Text = kritik.ToString();
            lblFragOrtaVal.Text   = orta.ToString();
            lblFragIyiVal.Text    = iyi.ToString();
        }

        private void gridViewFrag_CustomDrawCell(object sender, RowCellCustomDrawEventArgs e)
        {
            if (e.Column.FieldName == "DURUM")
            {
                string val = e.CellValue?.ToString() ?? "";
                if (val == "Kritik")    e.Appearance.ForeColor = Color.FromArgb(231, 76, 60);
                else if (val == "Orta") e.Appearance.ForeColor = Color.FromArgb(243, 156, 18);
                else                    e.Appearance.ForeColor = Color.FromArgb(39, 174, 96);
            }
            else if (e.Column.FieldName == "FRAGMENTASYON")
            {
                if (e.CellValue == null || e.CellValue == DBNull.Value) return;
                double val = Convert.ToDouble(e.CellValue);
                if (val >= 30)       e.Appearance.ForeColor = Color.FromArgb(231, 76, 60);
                else if (val >= 10)  e.Appearance.ForeColor = Color.FromArgb(243, 156, 18);
                else                 e.Appearance.ForeColor = Color.FromArgb(39, 174, 96);
            }
        }

        private void gridViewFrag_DoubleClick(object sender, EventArgs e)
        {
            var row = GetFragFocusedRow();
            if (row == null) return;
            string durum = row["DURUM"].ToString();
            string oneri = durum == "Kritik" ? "Rebuild önerilir (≥%30)."
                         : durum == "Orta"   ? "Reorganize yeterli (%10–%30)."
                                             : "İşlem gerekmez (<%10).";
            XtraMessageBox.Show(
                $"Tablo  : {row["TABLO_ADI"]}\n" +
                $"Index  : {row["INDEX_ADI"]}\n" +
                $"Tip    : {row["INDEX_TIPI"]}\n" +
                $"Frag.  : %{Convert.ToDouble(row["FRAGMENTASYON"]):N1}\n" +
                $"Sayfa  : {row["SAYFA_SAYISI"]}\n" +
                $"Kayıt  : {row["KAYIT_SAYISI"]}\n\n" +
                $"Öneri  : {oneri}",
                "Index Detayı", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void btnFragYenile_Click(object sender, EventArgs e)
        {
            string db = secilenDb ?? gridView1.GetFocusedRowCellValue("NAME")?.ToString();
            if (string.IsNullOrEmpty(db))
            {
                XtraMessageBox.Show("Ana listeden bir veritabanı seçin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            LoadFragmentasyon(db);
        }

        private void btnFragRebuildSec_Click(object sender, EventArgs e)
        {
            var row = GetFragFocusedRow();
            if (row == null) { XtraMessageBox.Show("Bir index seçin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string db = lblFragDbName.Text.Contains(":") ? lblFragDbName.Text.Split('|')[0].Replace("Veritabanı:", "").Trim() : secilenDb;
            if (XtraMessageBox.Show($"'{row["INDEX_ADI"]}' indexi REBUILD edilecek.\nDevam edilsin mi?", "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            RunFragOp(() => dbHelper.RebuildIndex(db, row["TABLO_ADI"].ToString(), row["INDEX_ADI"].ToString()), db);
        }

        private void btnFragReorgSec_Click(object sender, EventArgs e)
        {
            var row = GetFragFocusedRow();
            if (row == null) { XtraMessageBox.Show("Bir index seçin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string db = lblFragDbName.Text.Contains(":") ? lblFragDbName.Text.Split('|')[0].Replace("Veritabanı:", "").Trim() : secilenDb;
            if (XtraMessageBox.Show($"'{row["INDEX_ADI"]}' indexi REORGANIZE edilecek.\nDevam edilsin mi?", "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            RunFragOp(() => dbHelper.ReorganizeIndex(db, row["TABLO_ADI"].ToString(), row["INDEX_ADI"].ToString()), db);
        }

        private void btnFragRebuildKritik_Click(object sender, EventArgs e)
        {
            if (fragData == null) return;
            string db = secilenDb;
            var kritikler = fragData.AsEnumerable().Where(r => r["DURUM"].ToString() == "Kritik").ToList();
            if (kritikler.Count == 0) { XtraMessageBox.Show("Kritik index bulunamadı.", "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (XtraMessageBox.Show($"{kritikler.Count} kritik index rebuild edilecek.\nDevam edilsin mi?", "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            RunFragBatch(kritikler, r => dbHelper.RebuildIndex(db, r["TABLO_ADI"].ToString(), r["INDEX_ADI"].ToString()), db);
        }

        private void btnFragRebuildTumu_Click(object sender, EventArgs e)
        {
            if (fragData == null) return;
            string db = secilenDb;
            var hepsi = fragData.AsEnumerable().ToList();
            if (XtraMessageBox.Show($"{hepsi.Count} index rebuild edilecek. Emin misiniz?", "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            RunFragBatch(hepsi, r => dbHelper.RebuildIndex(db, r["TABLO_ADI"].ToString(), r["INDEX_ADI"].ToString()), db);
        }

        private void btnFragExcel_Click(object sender, EventArgs e)
        {
            GridHelper.ExportExcel(gridViewFrag, $"Fragmentasyon_{secilenDb}");
        }

        private DataRow GetFragFocusedRow()
        {
            int h = gridViewFrag.FocusedRowHandle;
            if (fragData == null || h < 0 || h >= fragData.Rows.Count) return null;
            return fragData.Rows[h];
        }

        private void RunFragOp(Action op, string db)
        {
            SetFragBusy(true);
            SetStatus("İşleniyor...");
            System.Threading.Tasks.Task.Run(op).ContinueWith(t =>
                Invoke(new Action(() =>
                {
                    SetFragBusy(false);
                    if (t.Exception != null) SetStatus("Hata: " + t.Exception.InnerException?.Message);
                    else LoadFragmentasyon(db);
                })));
        }

        private void RunFragBatch(List<DataRow> rows, Action<DataRow> op, string db)
        {
            SetFragBusy(true);
            progressBarFrag.Maximum = rows.Count;
            progressBarFrag.Value   = 0;
            progressBarFrag.Visible = true;
            int done = 0, err = 0;
            System.Threading.Tasks.Task.Run(() =>
            {
                foreach (var r in rows)
                {
                    try { op(r); } catch { err++; }
                    done++;
                    Invoke(new Action(() => { progressBarFrag.Value = done; SetStatus($"{done}/{rows.Count} işlendi..."); }));
                }
            }).ContinueWith(t => Invoke(new Action(() =>
            {
                progressBarFrag.Visible = false;
                SetFragBusy(false);
                SetStatus($"Tamamlandı: {done - err} başarılı, {err} hatalı.");
                LoadFragmentasyon(db);
            })));
        }

        private void SetFragBusy(bool busy)
        {
            btnFragYenile.Enabled        = !busy;
            btnFragRebuildSec.Enabled    = !busy;
            btnFragReorgSec.Enabled      = !busy;
            btnFragRebuildKritik.Enabled = !busy;
            btnFragRebuildTumu.Enabled   = !busy;
        }

        // ══════════════════════════════════════════════════════════════════════
        // YARDIMCI
        // ══════════════════════════════════════════════════════════════════════

        private void SetStatus(string message)
        {
            statusLabel.Text = "  " + message;
        }

        // ══════════════════════════════════════════════════════════════════════
        // SHRINK
        // ══════════════════════════════════════════════════════════════════════

        private void cmsShrinkDb_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(secilenDb)) return;
            DoShrinkDb(secilenDb);
        }

        private void cmsShrinkLog_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(secilenDb)) return;
            DoShrinkLog(secilenDb);
        }

        private void ShowShrinkDialog()
        {
            string db = secilenDb;
            if (string.IsNullOrEmpty(db))
            {
                XtraMessageBox.Show("Lütfen önce bir veritabanı seçin.", "Uyarı",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var form = new XtraForm
            {
                Text = $"Shrink — {db}",
                Width = 420, Height = 200,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
            };

            var lblInfo = new System.Windows.Forms.Label
            {
                Text = "Shrink işlemi index fragmentasyonuna yol açar.\nSonrasında index rebuild yapmanız önerilir.",
                Location = new Point(12, 14), Size = new System.Drawing.Size(380, 36),
                ForeColor = Color.FromArgb(231, 76, 60),
                Font = new System.Drawing.Font("Segoe UI", 9F),
            };

            var lblPct = new LabelControl { Text = "Hedef boş alan %:", Location = new Point(12, 62), AutoSizeMode = LabelAutoSizeMode.Horizontal };
            var nudPct = new DevExpress.XtraEditors.SpinEdit { Location = new Point(130, 58), Size = new System.Drawing.Size(70, 22) };
            nudPct.Properties.MinValue = 0; nudPct.Properties.MaxValue = 50; nudPct.Value = 10;

            var btnDb  = new SimpleButton { Text = "Veritabanı Shrink", Location = new Point(12, 98),  Width = 155, Height = 32 };
            var btnLog = new SimpleButton { Text = "Log Shrink",        Location = new Point(175, 98), Width = 110, Height = 32 };
            var btnKapat = new SimpleButton { Text = "Kapat",           Location = new Point(293, 98), Width = 80,  Height = 32 };

            btnDb.Appearance.BackColor = System.Drawing.Color.FromArgb(192, 57, 43);
            btnDb.Appearance.ForeColor = System.Drawing.Color.White;
            btnDb.Appearance.Options.UseBackColor = true; btnDb.Appearance.Options.UseForeColor = true;
            btnDb.LookAndFeel.UseDefaultLookAndFeel = false; btnDb.LookAndFeel.Style = DevExpress.LookAndFeel.LookAndFeelStyle.Flat;

            btnLog.Appearance.BackColor = System.Drawing.Color.FromArgb(243, 156, 18);
            btnLog.Appearance.ForeColor = System.Drawing.Color.White;
            btnLog.Appearance.Options.UseBackColor = true; btnLog.Appearance.Options.UseForeColor = true;
            btnLog.LookAndFeel.UseDefaultLookAndFeel = false; btnLog.LookAndFeel.Style = DevExpress.LookAndFeel.LookAndFeelStyle.Flat;

            btnDb.Click  += (s, e) => { form.Close(); DoShrinkDb(db, (int)nudPct.Value); };
            btnLog.Click += (s, e) => { form.Close(); DoShrinkLog(db); };
            btnKapat.Click += (s, e) => form.Close();

            form.Controls.AddRange(new Control[] { lblInfo, lblPct, nudPct, btnDb, btnLog, btnKapat });
            form.ShowDialog(this);
        }

        private void DoShrinkDb(string dbName, int targetPercent = 10)
        {
            SetStatus($"'{dbName}' shrink (DB) yapılıyor...");
            System.Threading.Tasks.Task.Run(() => dbHelper.ShrinkDatabase(dbName, targetPercent))
                .ContinueWith(t => Invoke(new Action(() =>
                {
                    if (t.Exception != null)
                        XtraMessageBox.Show("Shrink hatası:\n" + t.Exception.InnerException?.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    else
                    {
                        SetStatus($"'{dbName}' DB shrink tamamlandı. Index rebuild önerilir.");
                        XtraMessageBox.Show(
                            $"'{dbName}' veritabanı shrink tamamlandı.\n\nIndex fragmentasyonu oluşmuş olabilir.\nFragmentasyon tabından kontrol etmeniz önerilir.",
                            "Shrink Tamamlandı", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        LoadDatabases();
                    }
                })));
        }

        private void DoShrinkLog(string dbName)
        {
            SetStatus($"'{dbName}' shrink (Log) yapılıyor...");
            System.Threading.Tasks.Task.Run(() => dbHelper.ShrinkLog(dbName))
                .ContinueWith(t => Invoke(new Action(() =>
                {
                    if (t.Exception != null)
                        XtraMessageBox.Show("Log shrink hatası:\n" + t.Exception.InnerException?.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    else
                    {
                        SetStatus($"'{dbName}' log shrink tamamlandı.");
                        XtraMessageBox.Show($"'{dbName}' log dosyası shrink tamamlandı.", "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        LoadDatabases();
                    }
                })));
        }

        // ══════════════════════════════════════════════════════════════════════
        // AKTİF BAĞLANTILAR
        // ══════════════════════════════════════════════════════════════════════

        private void LoadActiveConnections()
        {
            SetStatus("Aktif bağlantılar yükleniyor...");
            System.Threading.Tasks.Task.Run(() => dbHelper.GetActiveConnections())
                .ContinueWith(t => Invoke(new Action(() =>
                {
                    if (t.Exception != null) { SetStatus("Hata: " + t.Exception.InnerException?.Message); return; }
                    gridControlBaglantilar.DataSource = null;
                    gridControlBaglantilar.DataSource = t.Result;
                    ConfigureBaglantıColumns();
                    SetStatus($"{t.Result?.Rows.Count ?? 0} aktif bağlantı listelendi.");
                })));
        }

        private void ConfigureBaglantıColumns()
        {
            if (gridViewBaglantilar.Columns.Count == 0) return;
            SetCol(gridViewBaglantilar, "SESSION_ID",      "Session",       65);
            SetCol(gridViewBaglantilar, "KULLANICI",       "Kullanıcı",    140);
            SetCol(gridViewBaglantilar, "MAKINE",          "Makine",       130);
            SetCol(gridViewBaglantilar, "UYGULAMA",        "Uygulama",     160);
            SetCol(gridViewBaglantilar, "VERITABANI",      "Veritabanı",   130);
            SetCol(gridViewBaglantilar, "DURUM",           "Durum",         80);
            SetCol(gridViewBaglantilar, "SURE_SN",         "Süre (sn)",     80);
            SetCol(gridViewBaglantilar, "CPU_MS",          "CPU (ms)",      80);
            SetCol(gridViewBaglantilar, "BELLEK_KB",       "Bellek (KB)",   90);
            SetCol(gridViewBaglantilar, "MANTIKSAL_OKUMA", "Man. Okuma",    95);
            SetCol(gridViewBaglantilar, "BLOKLAYAN",       "Bloklayan",     75);
            SetCol(gridViewBaglantilar, "BEKLEME_TIPI",    "Bekleme Tipi", 120);
        }

        private void gridViewBaglantilar_CustomDrawCell(object sender, DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e)
        {
            if (e.Column.FieldName == "DURUM")
            {
                string v = e.CellValue?.ToString() ?? "";
                if (v == "running")  e.Appearance.ForeColor = Color.FromArgb(39, 174, 96);
                else if (v == "sleeping") e.Appearance.ForeColor = Color.FromArgb(120, 120, 120);
                else e.Appearance.ForeColor = Color.FromArgb(243, 156, 18);
            }
            else if (e.Column.FieldName == "BLOKLAYAN" && e.CellValue?.ToString() != "—")
                e.Appearance.ForeColor = Color.FromArgb(231, 76, 60);
        }

        private void btnNavBaglantilar_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabPageBaglantilar;
            LoadActiveConnections();
        }

        private void btnBaglYenile_Click(object sender, EventArgs e) => LoadActiveConnections();

        private void btnBaglSonlandir_Click(object sender, EventArgs e)
        {
            int h = gridViewBaglantilar.FocusedRowHandle;
            if (h < 0) { XtraMessageBox.Show("Bir oturum seçin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            int sid = Convert.ToInt32(gridViewBaglantilar.GetRowCellValue(h, "SESSION_ID"));
            if (XtraMessageBox.Show($"Session {sid} sonlandırılsın mı?", "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                dbHelper.GetDataTable($"KILL {sid}");
                SetStatus($"Session {sid} sonlandırıldı.");
                LoadActiveConnections();
            }
            catch (Exception ex) { XtraMessageBox.Show("Hata:\n" + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // CANLI SORGULAR
        // ══════════════════════════════════════════════════════════════════════

        private void LoadLongRunningQueries()
        {
            SetStatus("Canlı sorgular yükleniyor...");
            System.Threading.Tasks.Task.Run(() => dbHelper.GetLongRunningQueries())
                .ContinueWith(t => Invoke(new Action(() =>
                {
                    if (t.Exception != null) { SetStatus("Hata: " + t.Exception.InnerException?.Message); return; }
                    gridControlSorgular.DataSource = null;
                    gridControlSorgular.DataSource = t.Result;
                    ConfigureSorguColumns();
                    SetStatus($"{t.Result?.Rows.Count ?? 0} aktif sorgu listelendi.");
                })));
        }

        private void ConfigureSorguColumns()
        {
            if (gridViewSorgular.Columns.Count == 0) return;
            SetCol(gridViewSorgular, "SESSION_ID",      "Session",       65);
            SetCol(gridViewSorgular, "KULLANICI",       "Kullanıcı",    130);
            SetCol(gridViewSorgular, "VERITABANI",      "Veritabanı",   120);
            SetCol(gridViewSorgular, "SURE_SN",         "Süre (sn)",     80);
            SetCol(gridViewSorgular, "DURUM",           "Durum",         80);
            SetCol(gridViewSorgular, "CPU_MS",          "CPU (ms)",      80);
            SetCol(gridViewSorgular, "MANTIKSAL_OKUMA", "Man. Okuma",    95);
            SetCol(gridViewSorgular, "BEKLEME_TIPI",    "Bekleme Tipi", 110);
            SetCol(gridViewSorgular, "BLOKLAYAN",       "Bloklayan",     75);
            SetCol(gridViewSorgular, "SORGU_METNI",     "Sorgu",        400);
        }

        private void gridViewSorgular_CustomDrawCell(object sender, DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e)
        {
            if (e.Column.FieldName == "SURE_SN" && e.CellValue != null && e.CellValue != DBNull.Value)
            {
                int sn = Convert.ToInt32(e.CellValue);
                if (sn > 60)       e.Appearance.ForeColor = Color.FromArgb(231, 76, 60);
                else if (sn > 10)  e.Appearance.ForeColor = Color.FromArgb(243, 156, 18);
            }
            else if (e.Column.FieldName == "BLOKLAYAN" && e.CellValue?.ToString() != "—")
                e.Appearance.ForeColor = Color.FromArgb(231, 76, 60);
        }

        private void btnNavSorgular_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabPageSorgular;
            LoadLongRunningQueries();
        }

        private void btnSorgYenile_Click(object sender, EventArgs e) => LoadLongRunningQueries();

        // ══════════════════════════════════════════════════════════════════════
        // KİLİTLER
        // ══════════════════════════════════════════════════════════════════════

        private void LoadLocks()
        {
            SetStatus("Kilit bilgisi yükleniyor...");
            System.Threading.Tasks.Task.Run(() => dbHelper.GetLockInfo())
                .ContinueWith(t => Invoke(new Action(() =>
                {
                    if (t.Exception != null) { SetStatus("Hata: " + t.Exception.InnerException?.Message); return; }
                    gridControlKilitler.DataSource = null;
                    gridControlKilitler.DataSource = t.Result;
                    ConfigureKilitColumns();
                    int blk = t.Result?.AsEnumerable().Count(r => r["BLOKLAYAN"]?.ToString() != "—") ?? 0;
                    SetStatus($"{t.Result?.Rows.Count ?? 0} kilit, {blk} bloklayan.");
                    if (blk > 0) notifyIcon1.ShowBalloonTip(3000, "DB Manager — Uyarı", $"{blk} bloklayan kilit tespit edildi!", System.Windows.Forms.ToolTipIcon.Warning);
                })));
        }

        private void ConfigureKilitColumns()
        {
            if (gridViewKilitler.Columns.Count == 0) return;
            SetCol(gridViewKilitler, "SESSION_ID",    "Session",       65);
            SetCol(gridViewKilitler, "KULLANICI",     "Kullanıcı",    130);
            SetCol(gridViewKilitler, "VERITABANI",    "Veritabanı",   120);
            SetCol(gridViewKilitler, "KAYNAK_TIPI",   "Kaynak Tipi",  100);
            SetCol(gridViewKilitler, "KAYNAK",        "Kaynak",       150);
            SetCol(gridViewKilitler, "KILIT_MODU",    "Kilit Modu",    90);
            SetCol(gridViewKilitler, "DURUM",         "Durum",         80);
            SetCol(gridViewKilitler, "BLOKLAYAN",     "Bloklayan",     80);
            SetCol(gridViewKilitler, "BEKLEME_MS",    "Bekleme (ms)",  90);
            SetCol(gridViewKilitler, "BEKLEME_TIPI",  "Bekleme Tipi", 120);
        }

        private void gridViewKilitler_CustomDrawCell(object sender, DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e)
        {
            if (e.Column.FieldName == "BLOKLAYAN" && e.CellValue?.ToString() != "—")
                e.Appearance.ForeColor = Color.FromArgb(231, 76, 60);
            else if (e.Column.FieldName == "DURUM")
            {
                if (e.CellValue?.ToString() == "WAIT") e.Appearance.ForeColor = Color.FromArgb(243, 156, 18);
                else if (e.CellValue?.ToString() == "GRANT") e.Appearance.ForeColor = Color.FromArgb(39, 174, 96);
            }
        }

        private void btnNavKilitler_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabPageKilitler;
            LoadLocks();
        }

        private void btnKilitYenile_Click(object sender, EventArgs e) => LoadLocks();

        // ══════════════════════════════════════════════════════════════════════
        // TEMPDB
        // ══════════════════════════════════════════════════════════════════════

        private void LoadTempDb()
        {
            SetStatus("TempDB kullanımı yükleniyor...");
            System.Threading.Tasks.Task.Run(() => dbHelper.GetTempDbUsage())
                .ContinueWith(t => Invoke(new Action(() =>
                {
                    if (t.Exception != null) { SetStatus("Hata: " + t.Exception.InnerException?.Message); return; }
                    gridControlTempDb.DataSource = null;
                    gridControlTempDb.DataSource = t.Result;
                    ConfigureTempDbColumns();
                    SetStatus($"TempDB: {t.Result?.Rows.Count ?? 0} session kullanıyor.");
                })));
        }

        private void ConfigureTempDbColumns()
        {
            if (gridViewTempDb.Columns.Count == 0) return;
            SetCol(gridViewTempDb, "SESSION_ID",      "Session",        65);
            SetCol(gridViewTempDb, "KULLANICI",       "Kullanıcı",     130);
            SetCol(gridViewTempDb, "MAKINE",          "Makine",        120);
            SetCol(gridViewTempDb, "VERITABANI",      "Veritabanı",    120);
            SetCol(gridViewTempDb, "KULLANICI_OBJ_KB","Kullanıcı (KB)", 110);
            SetCol(gridViewTempDb, "DAHILI_OBJ_KB",  "Dahili (KB)",   100);
            SetCol(gridViewTempDb, "TOPLAM_KB",       "Toplam (KB)",   110);
        }

        private void btnNavTempDb_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabPageTempDb;
            LoadTempDb();
        }

        private void btnTempDbYenile_Click(object sender, EventArgs e) => LoadTempDb();

        // ══════════════════════════════════════════════════════════════════════
        // KULLANICILAR
        // ══════════════════════════════════════════════════════════════════════

        // Seçili DB'yi çözer: focused row → secilenDb → mainData'nın ilk satırı
        private string ResolveDb()
        {
            string db = gridView1.GetFocusedRowCellValue("NAME")?.ToString();
            if (string.IsNullOrEmpty(db)) db = secilenDb;
            if (string.IsNullOrEmpty(db) && mainData?.Rows.Count > 0)
                db = mainData.Rows[0]["NAME"].ToString();
            return db;
        }

        private void LoadDatabaseUsers()
        {
            string db = ResolveDb();
            if (string.IsNullOrEmpty(db))
            {
                SetStatus("Kullanıcılar için önce bir veritabanı seçin.");
                return;
            }
            SetStatus($"'{db}' kullanıcıları yükleniyor...");
            System.Threading.Tasks.Task.Run(() => dbHelper.GetDatabaseUsers(db))
                .ContinueWith(t => Invoke(new Action(() =>
                {
                    if (t.Exception != null) { SetStatus("Hata: " + t.Exception.InnerException?.Message); return; }
                    gridControlKullanicilar.DataSource = null;
                    gridControlKullanicilar.DataSource = t.Result;
                    ConfigureKullaniciColumns();
                    SetStatus($"'{db}': {t.Result?.Rows.Count ?? 0} kullanıcı listelendi.");
                })));
        }

        private void ConfigureKullaniciColumns()
        {
            if (gridViewKullanicilar.Columns.Count == 0) return;
            SetCol(gridViewKullanicilar, "KULLANICI_ADI", "Kullanıcı Adı", 180);
            SetCol(gridViewKullanicilar, "LOGIN_ADI",     "Login",          160);
            SetCol(gridViewKullanicilar, "AUTH_TIP",      "Auth Tipi",       90);
            SetCol(gridViewKullanicilar, "ROLLER",        "Roller",          250);
            SetCol(gridViewKullanicilar, "OLUSTURMA",     "Oluşturulma",     130);
            SetCol(gridViewKullanicilar, "DEGISTIRME",    "Değiştirilme",    130);
        }

        private void btnNavKullanicilar_Click(object sender, EventArgs e)
        {
            secilenDb = ResolveDb();
            tabMain.SelectedTab = tabPageKullanicilar;
            LoadDatabaseUsers();
        }

        private void btnKulYenile_Click(object sender, EventArgs e) => LoadDatabaseUsers();

        private void cmsKullanicilar_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(secilenDb)) return;
            tabMain.SelectedTab = tabPageKullanicilar;
            LoadDatabaseUsers();
        }

        // ══════════════════════════════════════════════════════════════════════
        // SCRIPT ÜRET
        // ══════════════════════════════════════════════════════════════════════

        private void cmsScriptUret_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(secilenDb)) return;
            try
            {
                string script = dbHelper.GenerateDatabaseScript(secilenDb);
                var form = new XtraForm
                {
                    Text = $"Script — {secilenDb}",
                    Width = 700, Height = 480,
                    StartPosition = FormStartPosition.CenterParent,
                };
                var txt = new System.Windows.Forms.RichTextBox
                {
                    Dock = System.Windows.Forms.DockStyle.Fill,
                    Text = script,
                    Font = new System.Drawing.Font("Consolas", 10F),
                    ReadOnly = true,
                    BackColor = Color.FromArgb(24, 24, 27),
                    ForeColor = Color.FromArgb(220, 220, 220),
                };
                var btnKopyala = new SimpleButton { Text = "Panoya Kopyala", Dock = System.Windows.Forms.DockStyle.Bottom, Height = 34 };
                btnKopyala.Click += (s, ev) => { Clipboard.SetText(script); SetStatus("Script panoya kopyalandı."); };
                form.Controls.Add(txt);
                form.Controls.Add(btnKopyala);
                form.ShowDialog(this);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show("Script oluşturulamadı:\n" + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // ARAMA / FİLTRE
        // ══════════════════════════════════════════════════════════════════════

        private void txtArama_EditValueChanged(object sender, EventArgs e)
        {
            string q = txtArama.Text?.Trim() ?? "";
            if (mainData == null) return;
            if (string.IsNullOrEmpty(q))
            {
                gridControl1.DataSource = mainData;
                return;
            }
            var filtered = mainData.AsEnumerable()
                .Where(r => r["NAME"].ToString().IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            gridControl1.DataSource = filtered.Count > 0 ? filtered.CopyToDataTable() : mainData.Clone();
            SetStatus($"'{q}' araması: {filtered.Count} sonuç.");
        }

        private void btnAramaSifirla_Click(object sender, EventArgs e)
        {
            txtArama.EditValue = null;
            gridControl1.DataSource = mainData;
            SetStatus("Filtre temizlendi.");
        }

        // ══════════════════════════════════════════════════════════════════════
        // AYARLAR
        // ══════════════════════════════════════════════════════════════════════

        private void btnNavAyarlar_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabPageAyarlar;
            LoadSettingsToUI();
        }

        private void LoadSettingsToUI()
        {
            txtAyarConn.Text          = currentConnectionString;
            txtAyarBackupKlasor.Text  = ayarBackupKlasor;
            nudAyarBackupGun.Value    = ayarBackupUyariGun;
            nudAyarBoyutMb.Value      = ayarBoyutUyariMb;
            chkAyarOtomatikYenile.Checked = timerAutoRefresh.Enabled;
            nudAyarYenileSaniye.Value = timerAutoRefresh.Interval / 1000;
            lblAyarDurum.Text = "";
        }

        private void LoadAppSettings()
        {
            if (!File.Exists(SettingsFile)) return;
            foreach (string line in File.ReadAllLines(SettingsFile))
            {
                if (!line.Contains("=")) continue;
                int idx = line.IndexOf('=');
                string key = line.Substring(0, idx).Trim();
                string val = line.Substring(idx + 1).Trim();
                switch (key)
                {
                    case "ConnectionString":
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            currentConnectionString = val;
                            dbHelper.SetConnectionString(val);
                        }
                        break;
                    case "BackupKlasor":    ayarBackupKlasor    = val; break;
                    case "BackupUyariGun":  int.TryParse(val, out ayarBackupUyariGun); break;
                    case "BoyutUyariMb":    int.TryParse(val, out ayarBoyutUyariMb); break;
                    case "OtomatikYenile":
                        if (val == "1") { timerAutoRefresh.Start(); } break;
                    case "YenileSaniye":
                        if (int.TryParse(val, out int sn) && sn >= 10)
                            timerAutoRefresh.Interval = sn * 1000; break;
                }
            }
        }

        private void SaveAppSettings()
        {
            var lines = new List<string>
            {
                $"ConnectionString={currentConnectionString}",
                $"BackupKlasor={ayarBackupKlasor}",
                $"BackupUyariGun={ayarBackupUyariGun}",
                $"BoyutUyariMb={ayarBoyutUyariMb}",
                $"OtomatikYenile={( timerAutoRefresh.Enabled ? "1" : "0" )}",
                $"YenileSaniye={timerAutoRefresh.Interval / 1000}",
            };
            File.WriteAllLines(SettingsFile, lines);
        }

        private void btnAyarKaydet_Click(object sender, EventArgs e)
        {
            // Connection string değişti mi?
            string yeniConn = txtAyarConn.Text?.Trim();
            if (!string.IsNullOrEmpty(yeniConn) && yeniConn != currentConnectionString)
            {
                var tmp = new DatabaseHelper(yeniConn);
                if (!tmp.TestConnection())
                {
                    XtraMessageBox.Show("Bağlantı test edildi — başarısız. Connection string kaydedilmedi.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    currentConnectionString = yeniConn;
                    dbHelper.SetConnectionString(currentConnectionString);
                    LoadDatabases();
                }
            }
            ayarBackupKlasor    = txtAyarBackupKlasor.Text?.Trim() ?? "";
            ayarBackupUyariGun  = (int)nudAyarBackupGun.Value;
            ayarBoyutUyariMb    = (int)nudAyarBoyutMb.Value;
            timerAutoRefresh.Interval = (int)nudAyarYenileSaniye.Value * 1000;
            if (chkAyarOtomatikYenile.Checked) timerAutoRefresh.Start();
            else timerAutoRefresh.Stop();
            SaveAppSettings();
            lblAyarDurum.Text = "Ayarlar kaydedildi.";
            lblAyarDurum.Appearance.ForeColor = Color.FromArgb(39, 174, 96);
        }

        private void btnAyarBackupSec_Click(object sender, EventArgs e)
        {
            var dlg = new FolderBrowserDialog { ShowNewFolderButton = true };
            if (dlg.ShowDialog() == DialogResult.OK)
                txtAyarBackupKlasor.Text = dlg.SelectedPath;
        }

        private void chkAyarOtomatikYenile_CheckedChanged(object sender, EventArgs e)
        {
            nudAyarYenileSaniye.Enabled = chkAyarOtomatikYenile.Checked;
        }

        private void timerAutoRefresh_Tick(object sender, EventArgs e)
        {
            if (tabMain.SelectedTab == tabPageDBs)        LoadDatabases();
            else if (tabMain.SelectedTab == tabPageBaglantilar) LoadActiveConnections();
            else if (tabMain.SelectedTab == tabPageSorgular)    LoadLongRunningQueries();
            else if (tabMain.SelectedTab == tabPageKilitler)    LoadLocks();
        }

        // ══════════════════════════════════════════════════════════════════════
        // UYARILER (Form Load sonrası)
        // ══════════════════════════════════════════════════════════════════════

        private void CheckWarnings(DataTable dt)
        {
            if (dt == null) return;

            // Offline DB uyarısı
            var offline = dt.AsEnumerable().Where(r => r["DURUM_KOD"].ToString() != "ONLINE").ToList();
            if (offline.Count > 0)
            {
                string names = string.Join(", ", offline.Select(r => r["NAME"].ToString()).Take(5));
                notifyIcon1.ShowBalloonTip(4000, "DB Manager — Çevrimdışı DB",
                    $"{offline.Count} veritabanı çevrimdışı: {names}", System.Windows.Forms.ToolTipIcon.Error);
            }

            // Backup uyarısı
            var eski = dt.AsEnumerable().Where(r =>
                r["SON_BACKUP"] == DBNull.Value ||
                (DateTime.Now - Convert.ToDateTime(r["SON_BACKUP"])).TotalDays > ayarBackupUyariGun).ToList();
            if (eski.Count > 0)
                SetStatus($"  ⚠ {eski.Count} veritabanında backup {ayarBackupUyariGun}+ gün eski veya yok!");

            // Boyut uyarısı
            if (ayarBoyutUyariMb > 0)
            {
                var buyuk = dt.AsEnumerable().Where(r =>
                    r["BOYUT_MB"] != DBNull.Value &&
                    Convert.ToDouble(r["BOYUT_MB"]) > ayarBoyutUyariMb).ToList();
                if (buyuk.Count > 0)
                    notifyIcon1.ShowBalloonTip(3000, "DB Manager — Boyut Uyarısı",
                        $"{buyuk.Count} veritabanı {ayarBoyutUyariMb:N0} MB eşiğini geçti.",
                        System.Windows.Forms.ToolTipIcon.Warning);
            }
        }
    }
}
