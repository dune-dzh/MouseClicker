namespace MouseClicker;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        statusLabel = new Label();
        filePathLabel = new Label();
        hotkeysLabel = new Label();
        configEditorTextBox = new RichTextBox();
        saveConfigButton = new Button();
        loadConfigButton = new Button();
        reloadConfigButton = new Button();
        pickMoveToButton = new Button();
        pickMoveAndLeftClickButton = new Button();
        pickerHintLabel = new Label();
        stepsListBox = new ListBox();
        logTextBox = new TextBox();
        SuspendLayout();
        // 
        // statusLabel
        // 
        statusLabel.AutoSize = true;
        statusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 0);
        statusLabel.Location = new Point(12, 9);
        statusLabel.Name = "statusLabel";
        statusLabel.Size = new Size(86, 15);
        statusLabel.TabIndex = 0;
        statusLabel.Text = "Status: Stopped";
        // 
        // filePathLabel
        // 
        filePathLabel.AutoSize = true;
        filePathLabel.Location = new Point(12, 30);
        filePathLabel.Name = "filePathLabel";
        filePathLabel.Size = new Size(52, 15);
        filePathLabel.TabIndex = 1;
        filePathLabel.Text = "Config: -";
        // 
        // hotkeysLabel
        // 
        hotkeysLabel.AutoSize = true;
        hotkeysLabel.Location = new Point(12, 47);
        hotkeysLabel.Name = "hotkeysLabel";
        hotkeysLabel.Size = new Size(204, 15);
        hotkeysLabel.TabIndex = 2;
        hotkeysLabel.Text = "Hotkeys: F6 Start/Stop | F7 Pick | F12 Kill | Editor: / commands";
        // 
        // configEditorTextBox
        // 
        configEditorTextBox.DetectUrls = false;
        configEditorTextBox.Font = new Font("Consolas", 9F);
        configEditorTextBox.Location = new Point(12, 68);
        configEditorTextBox.Name = "configEditorTextBox";
        configEditorTextBox.ScrollBars = RichTextBoxScrollBars.Vertical;
        configEditorTextBox.ShortcutsEnabled = false;
        configEditorTextBox.Size = new Size(380, 168);
        configEditorTextBox.TabIndex = 3;
        configEditorTextBox.WordWrap = false;
        // 
        // saveConfigButton
        // 
        saveConfigButton.Location = new Point(12, 244);
        saveConfigButton.Name = "saveConfigButton";
        saveConfigButton.Size = new Size(115, 27);
        saveConfigButton.TabIndex = 4;
        saveConfigButton.Text = "Save Config";
        saveConfigButton.UseVisualStyleBackColor = true;
        saveConfigButton.Click += saveConfigButton_Click;
        // 
        // loadConfigButton
        // 
        loadConfigButton.Location = new Point(133, 244);
        loadConfigButton.Name = "loadConfigButton";
        loadConfigButton.Size = new Size(115, 27);
        loadConfigButton.TabIndex = 11;
        loadConfigButton.Text = "Load File";
        loadConfigButton.UseVisualStyleBackColor = true;
        loadConfigButton.Click += loadConfigButton_Click;
        // 
        // reloadConfigButton
        // 
        reloadConfigButton.Location = new Point(254, 244);
        reloadConfigButton.Name = "reloadConfigButton";
        reloadConfigButton.Size = new Size(115, 27);
        reloadConfigButton.TabIndex = 5;
        reloadConfigButton.Text = "Reload File";
        reloadConfigButton.UseVisualStyleBackColor = true;
        reloadConfigButton.Click += reloadConfigButton_Click;
        // 
        // pickMoveToButton
        // 
        pickMoveToButton.Location = new Point(12, 277);
        pickMoveToButton.Name = "pickMoveToButton";
        pickMoveToButton.Size = new Size(180, 27);
        pickMoveToButton.TabIndex = 6;
        pickMoveToButton.Text = "Pick MoveTo";
        pickMoveToButton.UseVisualStyleBackColor = true;
        pickMoveToButton.Click += pickMoveToButton_Click;
        // 
        // pickMoveAndLeftClickButton
        // 
        pickMoveAndLeftClickButton.Location = new Point(198, 277);
        pickMoveAndLeftClickButton.Name = "pickMoveAndLeftClickButton";
        pickMoveAndLeftClickButton.Size = new Size(171, 27);
        pickMoveAndLeftClickButton.TabIndex = 7;
        pickMoveAndLeftClickButton.Text = "Pick Move+Click";
        pickMoveAndLeftClickButton.UseVisualStyleBackColor = true;
        pickMoveAndLeftClickButton.Click += pickMoveAndLeftClickButton_Click;
        // 
        // pickerHintLabel
        // 
        pickerHintLabel.AutoSize = true;
        pickerHintLabel.Location = new Point(12, 310);
        pickerHintLabel.Name = "pickerHintLabel";
        pickerHintLabel.Size = new Size(145, 15);
        pickerHintLabel.TabIndex = 8;
        pickerHintLabel.Text = "Picker idle (use Pick button)";
        // 
        // stepsListBox
        // 
        stepsListBox.FormattingEnabled = true;
        stepsListBox.Location = new Point(398, 68);
        stepsListBox.Name = "stepsListBox";
        stepsListBox.Size = new Size(374, 236);
        stepsListBox.TabIndex = 9;
        // 
        // logTextBox
        // 
        logTextBox.Location = new Point(12, 331);
        logTextBox.Multiline = true;
        logTextBox.Name = "logTextBox";
        logTextBox.ReadOnly = true;
        logTextBox.ScrollBars = ScrollBars.Vertical;
        logTextBox.Size = new Size(760, 108);
        logTextBox.TabIndex = 10;
        // 
        // Form1
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(784, 451);
        MinimumSize = new Size(800, 480);
        Controls.Add(logTextBox);
        Controls.Add(stepsListBox);
        Controls.Add(pickerHintLabel);
        Controls.Add(pickMoveAndLeftClickButton);
        Controls.Add(pickMoveToButton);
        Controls.Add(reloadConfigButton);
        Controls.Add(loadConfigButton);
        Controls.Add(saveConfigButton);
        Controls.Add(configEditorTextBox);
        Controls.Add(hotkeysLabel);
        Controls.Add(filePathLabel);
        Controls.Add(statusLabel);
        MaximizeBox = false;
        Name = "Form1";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Mouse Clicker (F6 Start/Stop)";
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label statusLabel;
    private Label filePathLabel;
    private Label hotkeysLabel;
    private RichTextBox configEditorTextBox;
    private Button saveConfigButton;
    private Button loadConfigButton;
    private Button reloadConfigButton;
    private Button pickMoveToButton;
    private Button pickMoveAndLeftClickButton;
    private Label pickerHintLabel;
    private ListBox stepsListBox;
    private TextBox logTextBox;
}
