using System;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

class HashChecker : Form
{
    static readonly string[] Names = { "MD5", "SHA-1", "SHA-256", "SHA-512" };

    Label fileLabel, infoLabel, resultLabel;
    TextBox[] hashBoxes = new TextBox[4];
    TextBox expectedBox;
    ProgressBar progress;
    Button openButton;
    Thread worker;
    volatile bool cancel;

    HashChecker(string startFile, string expected)
    {
        Text = "HashChecker";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(700, 360);
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
        DragDrop += (s, e) => { var files = (string[])e.Data.GetData(DataFormats.FileDrop); if (files.Length > 0) Start(files[0]); };

        openButton = new Button { Text = "Выбрать файл…", Location = new Point(12, 12), Size = new Size(140, 32) };
        openButton.Click += (s, e) => Browse();
        Controls.Add(openButton);
        fileLabel = new Label { Text = "или перетащи файл в это окно", Location = new Point(162, 11), Size = new Size(526, 20), AutoEllipsis = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        Controls.Add(fileLabel);
        infoLabel = new Label { Location = new Point(162, 31), Size = new Size(526, 18), ForeColor = Color.Gray, Font = new Font("Segoe UI", 9) };
        Controls.Add(infoLabel);

        for (int i = 0; i < 4; i++)
        {
            int y = 62 + i * 36;
            Controls.Add(new Label { Text = Names[i] + ":", Location = new Point(12, y + 3), AutoSize = true });
            hashBoxes[i] = new TextBox { Location = new Point(90, y), Width = 510, ReadOnly = true, Font = new Font("Consolas", 9.5f), BackColor = SystemColors.Window };
            Controls.Add(hashBoxes[i]);
            var box = hashBoxes[i];
            var copy = new Button { Text = "Копировать", Location = new Point(606, y - 1), Size = new Size(82, 27), Font = new Font("Segoe UI", 8.5f) };
            copy.Click += (s, e) => { if (box.Text.Length > 0) Clipboard.SetText(box.Text); };
            Controls.Add(copy);
        }

        progress = new ProgressBar { Location = new Point(12, 212), Size = new Size(676, 8) };
        Controls.Add(progress);

        Controls.Add(new Label { Text = "Сверить с хэшем с сайта (вставь сюда):", Location = new Point(12, 234), AutoSize = true });
        expectedBox = new TextBox { Location = new Point(12, 258), Width = 676, Font = new Font("Consolas", 9.5f) };
        expectedBox.TextChanged += (s, e) => Compare();
        Controls.Add(expectedBox);
        resultLabel = new Label { Location = new Point(12, 296), Size = new Size(676, 50), Font = new Font("Segoe UI", 12, FontStyle.Bold) };
        Controls.Add(resultLabel);

        if (expected != null) expectedBox.Text = expected;
        if (startFile != null) Shown += (s, e) => Start(startFile);
    }

    void Browse()
    {
        using (var d = new OpenFileDialog())
            if (d.ShowDialog(this) == DialogResult.OK) Start(d.FileName);
    }

    void Start(string path)
    {
        if (!File.Exists(path)) return;
        if (worker != null && worker.IsAlive) { cancel = true; worker.Join(); }
        cancel = false;
        foreach (var b in hashBoxes) b.Text = "";
        fileLabel.Text = Path.GetFileName(path);
        infoLabel.Text = FormatSize(new FileInfo(path).Length) + "   ·   " + Path.GetDirectoryName(path);
        resultLabel.Text = "Считаю…";
        resultLabel.ForeColor = Color.Gray;
        worker = new Thread(() => Hash(path)) { IsBackground = true };
        worker.Start();
    }

    void Hash(string path)
    {
        HashAlgorithm[] algs = { MD5.Create(), SHA1.Create(), SHA256.Create(), SHA512.Create() };
        try
        {
            using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20))
            {
                var buf = new byte[1 << 20];
                long total = Math.Max(1, s.Length), done = 0;
                int n, lastPercent = -1;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    if (cancel) return;
                    foreach (var a in algs) a.TransformBlock(buf, 0, n, null, 0);
                    done += n;
                    int p = (int)(done * 100 / total);
                    if (p != lastPercent) { lastPercent = p; BeginInvoke(new Action(() => progress.Value = p)); }
                }
            }
            var results = new string[4];
            for (int i = 0; i < 4; i++)
            {
                algs[i].TransformFinalBlock(new byte[0], 0, 0);
                results[i] = BitConverter.ToString(algs[i].Hash).Replace("-", "").ToLowerInvariant();
            }
            BeginInvoke(new Action(() =>
            {
                for (int i = 0; i < 4; i++) hashBoxes[i].Text = results[i];
                progress.Value = 100;
                Compare();
            }));
        }
        catch (Exception ex)
        {
            BeginInvoke(new Action(() => { resultLabel.Text = "Не удалось прочитать файл: " + ex.Message; resultLabel.ForeColor = Color.Firebrick; }));
        }
        finally
        {
            foreach (var a in algs) a.Dispose();
        }
    }

    void Compare()
    {
        if (hashBoxes[0].Text.Length == 0) return;
        string expected = expectedBox.Text.Trim().Replace(" ", "").ToLowerInvariant();
        if (expected.Length == 0)
        {
            resultLabel.Text = "Хэши посчитаны. Вставь ожидаемый хэш, чтобы сверить";
            resultLabel.ForeColor = Color.Gray;
            return;
        }
        for (int i = 0; i < 4; i++)
            if (hashBoxes[i].Text == expected)
            {
                resultLabel.Text = "✔ Совпадает с " + Names[i] + ": файл не повреждён и не подменён";
                resultLabel.ForeColor = Color.ForestGreen;
                return;
            }
        resultLabel.Text = "✘ Не совпадает ни с одним хэшем. Файл повреждён или это не тот файл";
        resultLabel.ForeColor = Color.Firebrick;
    }

    static string FormatSize(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return (u == 0 ? v.ToString("0") : v.ToString("0.0")) + " " + units[u];
    }

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.Run(new HashChecker(args.Length > 0 && File.Exists(args[0]) ? args[0] : null, args.Length > 1 ? args[1] : null));
    }
}
