﻿/*
ImageGlass Project - Image viewer for Windows
Copyright (C) 2010 - 2023 DUONG DIEU PHAP
Project homepage: https://imageglass.org

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/
using ImageGlass.Base;
using ImageGlass.Base.Actions;
using ImageGlass.Base.DirectoryComparer;
using ImageGlass.Base.PhotoBox;
using ImageGlass.Base.Photoing.Codecs;
using ImageGlass.Base.WinApi;
using ImageGlass.Gallery;
using ImageGlass.Settings;
using ImageGlass.UI;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using WicNet;

namespace ImageGlass;

public partial class FrmMain : ThemedForm
{
    public readonly ModernToolbar ToolbarContext = new ModernToolbar();

    // cancellation tokens of synchronious task
    private CancellationTokenSource? _loadCancelTokenSrc = new();
    private IProgress<ProgressReporterEventArgs> _uiReporter;
    private MovableForm _movableForm;
    private bool _isShowingImagePreview = false;


    // variable to back up / restore window layout when changing window mode
    private bool _showToolbar = true;
    private bool _showThumbnails = true;
    private Rectangle _windowBound = new();
    private FormWindowState _windowState = FormWindowState.Normal;

    public FrmMain() : base()
    {
        InitializeComponent();
        InitializeToolbarContext();

        // initialize UI thread reporter
        _uiReporter = new Progress<ProgressReporterEventArgs>(ReportToUIThread);

        // update the DpiApi when DPI changed.
        EnableDpiApiUpdate = true;

        // update form settings according to user config
        SetUpFrmMainConfigs();

        // update theme icons
        OnDpiChanged();

        ApplyTheme(Config.Theme.Settings.IsDarkMode);
    }

    private void FrmMain_Load(object sender, EventArgs e)
    {
        SetupFileWatcher();


        Local.ImageTransform.Changed += ImageTransform_Changed;
        Application.ApplicationExit += Application_ApplicationExit;

        LoadImagesFromCmdArgs(Environment.GetCommandLineArgs());
    }

    protected override void OnDpiChanged()
    {
        base.OnDpiChanged();
        SuspendLayout();

        // scale toolbar icons corresponding to DPI
        var newIconHeight = this.ScaleToDpi(Config.ToolbarIconHeight);

        // reload theme
        Config.Theme.LoadTheme(newIconHeight);

        // update toolbar theme
        Toolbar.UpdateTheme(newIconHeight);
        ToolbarContext.UpdateTheme(newIconHeight);
        UpdatePageNavToolbarButtonState();

        // update picmain scaling
        PicMain.NavButtonSize = this.ScaleToDpi(new SizeF(50f, 50f));
        PicMain.CheckerboardCellSize = this.ScaleToDpi(Constants.VIEWER_GRID_SIZE);

        // gallery
        UpdateGallerySize();

        ResumeLayout(false);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);

        MnuMain.CurrentDpi =
            MnuContext.CurrentDpi =
            MnuSubMenu.CurrentDpi = e.DeviceDpiNew;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // to fix arrow keys sometimes does not regconize
        if (keyData == Keys.Up
            || keyData == Keys.Down
            || keyData == Keys.Left
            || keyData == Keys.Right)
        {
            FrmMain_KeyDown(this, new KeyEventArgs(keyData));

            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }


    private void Application_ApplicationExit(object? sender, EventArgs e)
    {
        DisposeFileWatcher();
    }


    private void FrmMain_KeyDown(object sender, KeyEventArgs e)
    {
        //Text = new Hotkey(e.KeyData).ToString() + " - " + e.KeyValue.ToString();

        var hotkey = new Hotkey(e.KeyData);
        var actions = Config.GetHotkeyActions(CurrentMenuHotkeys, hotkey);

        // open main menu
        if (actions.Contains(nameof(MnuMain)))
        {
            Toolbar.ShowMainMenu();
        }
        // pass the zooming/panning to PicMain for smooth transition
        else if (actions.Contains(nameof(MnuZoomIn))
            || actions.Contains(nameof(IG_ZoomIn))
            || actions.Contains(nameof(MnuZoomOut))
            || actions.Contains(nameof(IG_ZoomOut))

            || actions.Contains(nameof(MnuPanLeft))
            || actions.Contains(nameof(IG_PanLeft))
            || actions.Contains(nameof(MnuPanRight))
            || actions.Contains(nameof(IG_PanRight))
            || actions.Contains(nameof(MnuPanUp))
            || actions.Contains(nameof(IG_PanUp))
            || actions.Contains(nameof(MnuPanDown))
            || actions.Contains(nameof(IG_PanDown)))
        {
            PicMain_KeyDown(PicMain, e);
            return;
        }


        // Register and run MAIN MENU shortcuts
        #region Register and run MAIN MENU shortcuts

        bool CheckMenuShortcut(ToolStripMenuItem mnu)
        {
            var menuHotkeyList = Config.GetHotkey(CurrentMenuHotkeys, mnu.Name);
            var menuHotkey = menuHotkeyList.SingleOrDefault(k => k.KeyData == e.KeyData);

            if (menuHotkey != null)
            {
                // ignore invisible menu
                if (mnu.Visible) return false;

                if (mnu.HasDropDownItems)
                {
                    ShowSubMenu(mnu);
                }
                else
                {
                    mnu.PerformClick();
                }

                return true;
            }

            foreach (var child in mnu.DropDownItems.OfType<ToolStripMenuItem>())
            {
                CheckMenuShortcut(child);
            }

            return false;
        }


        // register context menu shortcuts
        foreach (var item in MnuMain.Items.OfType<ToolStripMenuItem>())
        {
            if (CheckMenuShortcut(item)) return;
        }
        #endregion
    }


    private void FrmMain_KeyUp(object sender, KeyEventArgs e)
    {
        var hotkey = new Hotkey(e.KeyData);
        var actions = Config.GetHotkeyActions(CurrentMenuHotkeys, hotkey);

        // pass the zooming/panning to PicMain for smooth transition
        if (actions.Contains(nameof(MnuZoomIn))
            || actions.Contains(nameof(IG_ZoomIn))
            || actions.Contains(nameof(MnuZoomOut))
            || actions.Contains(nameof(IG_ZoomOut))

            || actions.Contains(nameof(MnuPanLeft))
            || actions.Contains(nameof(IG_PanLeft))
            || actions.Contains(nameof(MnuPanRight))
            || actions.Contains(nameof(IG_PanRight))
            || actions.Contains(nameof(MnuPanUp))
            || actions.Contains(nameof(IG_PanUp))
            || actions.Contains(nameof(MnuPanDown))
            || actions.Contains(nameof(IG_PanDown)))
        {
            PicMain_KeyUp(PicMain, e);
            return;
        }
    }


    private void Gallery_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.Buttons == MouseButtons.Left)
        {
            if (e.Item.Index == Local.CurrentIndex)
            {
                PicMain.Refresh();
            }
            else
            {
                GoToImage(e.Item.Index);
            }
        }
    }

    private void Gallery_ItemTooltipShowing(object sender, ItemTooltipShowingEventArgs e)
    {
        var langPath = "_.Metadata";

        // build tooltip content
        var sb = new StringBuilder();
        sb.AppendLine(e.Item.FileName);
        sb.AppendLine($"{Config.Language[$"{langPath}._{nameof(IgMetadata.FileSize)}"]}: {e.Item.Details.FileSizeFormated}");
        sb.AppendLine($"{Config.Language[$"{langPath}._{nameof(IgMetadata.FileLastWriteTime)}"]}: {e.Item.Details.FileLastWriteTimeFormated}");
        var tooltipLinesCount = 4;

        // FrameCount
        if (e.Item.Details.FrameCount > 1)
        {
            sb.AppendLine($"{Config.Language[$"{langPath}._{nameof(IgMetadata.FrameCount)}"]}: {e.Item.Details.FrameCount}");
            tooltipLinesCount++;
        }

        // Rating
        var rating = BHelper.FormatStarRatingText(e.Item.Details.ExifRatingPercent);
        if (!string.IsNullOrEmpty(rating))
        {
            sb.AppendLine($"{Config.Language[$"{langPath}._{nameof(IgMetadata.ExifRatingPercent)}"]}: {rating}");
            tooltipLinesCount++;
        }

        // ColorSpace
        if (!string.IsNullOrEmpty(e.Item.Details.ColorSpace))
        {
            sb.AppendLine($"{Config.Language[$"{langPath}._{nameof(IgMetadata.ColorSpace)}"]}: {e.Item.Details.ColorSpace}");
            tooltipLinesCount++;
        }

        // ColorProfile
        if (!string.IsNullOrEmpty(e.Item.Details.ColorProfile))
        {
            sb.AppendLine($"{Config.Language[$"{langPath}._{nameof(IgMetadata.ColorProfile)}"]}: {e.Item.Details.ColorProfile}");
            tooltipLinesCount++;
        }

        e.TooltipContent = sb.ToString();
        e.TooltipTitle = e.Item.Text + $" ({e.Item.Details.OriginalWidth} x {e.Item.Details.OriginalHeight})";
        e.TooltipSize = (Gallery.Tooltip as ModernTooltip)?.CalculateSize(e.TooltipContent);
    }


    private void Toolbar_ItemClicked(object? sender, ToolStripItemClickedEventArgs e)
    {
        var tagModel = e.ClickedItem.Tag as ToolbarItemTagModel;
        if (tagModel == null) return;

        // execute action
        _ = ExecuteUserActionAsync(tagModel.OnClick);
    }



    #region Image Loading functions

    /// <summary>
    /// Load images from command line arguments
    /// (<see cref="Environment.GetCommandLineArgs"/>)
    /// </summary>
    /// <param name="args"></param>
    public void LoadImagesFromCmdArgs(string[] args)
    {
        var pathToLoad = string.Empty;

        if (args.Length >= 2)
        {
            // get path from params
            var cmdPath = args
                .Skip(1)
                .FirstOrDefault(i => !i.StartsWith(Constants.CONFIG_CMD_PREFIX));

            if (!string.IsNullOrEmpty(cmdPath))
            {
                pathToLoad = cmdPath;
            }
        }

        if (string.IsNullOrEmpty(pathToLoad)
            && Config.ShouldOpenLastSeenImage
            && BHelper.CheckPath(Config.LastSeenImagePath) == PathType.File)
        {
            pathToLoad = Config.LastSeenImagePath;
        }

        if (string.IsNullOrEmpty(pathToLoad)) return;


        // Start loading path
        _ = BHelper.RunAsThread(() => PrepareLoading(pathToLoad));
    }


    /// <summary>
    /// Prepare and loads images from the input path
    /// </summary>
    /// <param name="inputPath">
    /// The relative/absolute path of file/folder; or a protocol path
    /// </param>
    public void PrepareLoading(string inputPath)
    {
        var path = BHelper.ResolvePath(inputPath);
        if (string.IsNullOrEmpty(path)) return;

        var pathType = BHelper.CheckPath(path);
        if (pathType == PathType.Unknown) return;

        if (pathType == PathType.Dir)
        {
            _ = PrepareLoadingAsync(new string[] { inputPath }, string.Empty);
        }
        else
        {
            // load images list
            _ = LoadImageListAsync(new string[] { inputPath }, path);

            // load the current image
            _ = ViewNextCancellableAsync(0, filePath: path);
        }
    }


    /// <summary>
    /// Prepares and load images from the input paths
    /// </summary>
    public async Task PrepareLoadingAsync(string[] paths, string? currentFile = null)
    {
        var filePath = currentFile;

        if (string.IsNullOrEmpty(currentFile))
        {
            filePath = paths.AsParallel().FirstOrDefault(i => BHelper.CheckPath(i) == PathType.File);
            filePath = BHelper.ResolvePath(filePath);
        }

        if (string.IsNullOrEmpty(filePath))
        {
            // load images list
            await LoadImageListAsync(paths, currentFile ?? filePath);

            // load the current image
            await ViewNextCancellableAsync(0);
        }
        else
        {
            // load the current image
            _ = ViewNextCancellableAsync(0, filePath: filePath);

            // load images list
            _ = LoadImageListAsync(paths, currentFile ?? filePath);
        }
    }


    /// <summary>
    /// Load the images list.
    /// </summary>
    /// <param name="inputPaths">The list of files to load</param>
    /// <param name="currentFilePath">The image file path to view first</param>
    public async Task LoadImageListAsync(
        IEnumerable<string> inputPaths,
        string? currentFilePath)
    {
        if (!inputPaths.Any()) return;
        currentFilePath ??= string.Empty;

        await Task.Run(() =>
        {
            var allFilesToLoad = new HashSet<string>();
            var currentFile = currentFilePath;
            var hasInitFile = !string.IsNullOrEmpty(currentFile);


            // track paths loaded to prevent duplicates
            var pathsLoaded = new HashSet<string>();
            var firstPath = true;

            // Parse string to absolute path
            var paths = inputPaths.Select(item => BHelper.ResolvePath(item));

            // prepare the distinct dir list
            var distinctDirsList = BHelper.GetDistinctDirsFromPaths(paths);

            foreach (var aPath in distinctDirsList)
            {
                var pathType = BHelper.CheckPath(aPath);
                if (pathType == PathType.Unknown) continue;

                var dirPath = aPath;

                // path is directory
                if (pathType == PathType.Dir)
                {
                    // Issue #415: If the folder name ends in ALT+255 (alternate space),
                    // DirectoryInfo strips it.
                    if (!aPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    {
                        dirPath = aPath + Path.DirectorySeparatorChar;
                    }
                }
                // path is file
                else
                {
                    if (string.Equals(Path.GetExtension(aPath), ".lnk", StringComparison.CurrentCultureIgnoreCase))
                    {
                        dirPath = FileShortcutApi.GetTargetPathFromShortcut(aPath);
                    }
                    else
                    {
                        dirPath = Path.GetDirectoryName(aPath) ?? string.Empty;
                    }
                }


                // TODO: Currently only have the ability to watch a single path for changes!
                if (firstPath)
                {
                    firstPath = false;
                    StartFileWatcher(dirPath);

                    // Seek for explorer sort order
                    DetermineSortOrder(dirPath);
                }

                // KBR 20181004 Fix observed bug: dropping multiple files from the same path
                // would load ALL files in said path multiple times! Prevent loading the same
                // path more than once.
                if (!pathsLoaded.Contains(dirPath))
                {
                    pathsLoaded.Add(dirPath);

                    var imageFilenameList = LoadImageFilesFromDirectory(dirPath);
                    allFilesToLoad.UnionWith(imageFilenameList);
                }
            }


            Local.InitialInputPath = hasInitFile
                ? (distinctDirsList.Count > 0 ? distinctDirsList[0] : string.Empty)
                : currentFile;


            // sort list
            // NOTE: relies on LocalSetting.ActiveImageLoadingOrder been updated first!
            var sortedFilesList = BHelper.SortImageList(allFilesToLoad,
                Local.ActiveImageLoadingOrder,
                Local.ActiveImageLoadingOrderType,
                Config.ShouldGroupImagesByDirectory);

            // add to image list
            Local.InitImageList(sortedFilesList, distinctDirsList);

            // Find the index of current image
            UpdateCurrentIndex(currentFilePath);


            _uiReporter.Report(new(new ImageListLoadedEventArgs()
            {
                InitFilePath = currentFilePath,
            }, nameof(Local.RaiseImageListLoadedEvent)));
        });
    }


    /// <summary>
    /// Updates <see cref="Local.CurrentIndex"/> according to the context.
    /// </summary>
    /// <param name="currentFilePath"></param>
    private static void UpdateCurrentIndex(string? currentFilePath)
    {
        if (string.IsNullOrEmpty(currentFilePath))
        {
            Local.CurrentIndex = 0;
            return;
        }

        // this part of code fixes calls on legacy 8.3 filenames
        // (for example opening files from IBM Notes)
        var di = new DirectoryInfo(currentFilePath);
        currentFilePath = di.FullName;

        // Find the index of current image
        Local.CurrentIndex = Local.Images.IndexOf(currentFilePath);

        // KBR 20181009
        // Changing "include subfolder" setting could lose the "current" image. Prefer
        // not to report said image is "corrupt", merely reset the index in that case.
        // 1. Setting: "include subfolders: ON".
        //    Open image in folder with images in subfolders.
        // 2. Move to an image in a subfolder.
        // 3. Change setting "include subfolders: OFF".
        // Issue: the image in the subfolder is attempted to be shown,
        // declared as corrupt/missing.
        // Issue #481: the test is incorrect when imagelist is empty (i.e. attempt to
        // open single, hidden image with 'show hidden' OFF)
        if (Local.CurrentIndex == -1
            && Local.Images.Length > 0
            && !Local.Images.ContainsDirPathOf(currentFilePath))
        {
            Local.CurrentIndex = 0;
        }
    }


    /// <summary>
    /// Determine the image sort order/direction based on user settings
    /// or Windows Explorer sorting.
    /// <para>
    /// Side effects:
    /// </para>
    /// <list type="bullet">
    ///     <item>Updates <see cref="Local.ActiveImageLoadingOrder"/></item>
    ///     <item>Updates <see cref="Local.ActiveImageLoadingOrderType"/></item>
    /// </list>
    /// </summary>
    /// <param name="fullPath">
    /// Full path to file/folder(i.e. as comes in from drag-and-drop)
    /// </param>
    private static void DetermineSortOrder(string fullPath)
    {
        // Initialize to the user-configured sorting order. Fetching the Explorer sort
        // order may fail, or may be on an unsupported column.
        Local.ActiveImageLoadingOrder = Config.ImageLoadingOrder;
        Local.ActiveImageLoadingOrderType = Config.ImageLoadingOrderType;

        // Use File Explorer sort order if possible
        if (Config.ShouldUseExplorerSortOrder)
        {
            if (ExplorerSortOrder.GetExplorerSortOrder(fullPath, out var explorerOrder, out var isAscending))
            {
                if (explorerOrder != null)
                {
                    Local.ActiveImageLoadingOrder = explorerOrder.Value;
                }

                if (isAscending != null)
                {
                    Local.ActiveImageLoadingOrderType = isAscending.Value ? ImageOrderType.Asc : ImageOrderType.Desc;
                }
            }
        }
    }


    /// <summary>
    /// Sort and find all supported image from directory
    /// </summary>
    /// <param name="path">Image folder path</param>
    private static IEnumerable<string> LoadImageFilesFromDirectory(string path)
    {
        // Get files from dir
        return DirectoryFinder.FindFiles(path,
            Config.EnableRecursiveLoading,
            new Predicate<FileInfo>((FileInfo fi) =>
            {
                // KBR 20180607 Rework predicate to use a FileInfo instead of the filename.
                // By doing so, can use the attribute data already loaded into memory, 
                // instead of fetching it again (via File.GetAttributes). A re-fetch is
                // very slow across network paths. For me, improves image load from 4+ 
                // seconds to 0.4 seconds for a specific network path.
                if (fi.FullName == null)
                    return false;

                var extension = fi.Extension.ToLower();

                // checks if image is hidden and ignores it if so
                if (!Config.ShouldLoadHiddenImages)
                {
                    var attributes = fi.Attributes;
                    var isHidden = (attributes & FileAttributes.Hidden) != 0;
                    if (isHidden)
                    {
                        return false;
                    }
                }

                return extension.Length > 0 && Config.AllFormats.Contains(extension);
            }));
    }


    /// <summary>
    /// Clear and reload all thumbnails in gallery
    /// </summary>
    public void LoadGallery()
    {
        if (InvokeRequired)
        {
            Invoke(LoadGallery);
            return;
        }

        Gallery.SuspendLayout();
        Gallery.Items.Clear();

        var thumbSize = this.ScaleToDpi(Config.ThumbnailSize);
        Gallery.ThumbnailSize = new Size(thumbSize, thumbSize);


        foreach (string filename in Local.Images.FileNames)
        {
            Gallery.Items.Add(filename);
        }

        Gallery.ResumeLayout();
        UpdateGallerySize();

        SelectCurrentGalleryThumbnail();
    }


    /// <summary>
    /// Select current thumbnail
    /// </summary>
    public void SelectCurrentGalleryThumbnail()
    {
        if (InvokeRequired)
        {
            Invoke(new(SelectCurrentGalleryThumbnail));
            return;
        }

        if (Gallery.Items.Count > 0)
        {
            Gallery.ClearSelection();

            try
            {
                Gallery.Items[Local.CurrentIndex].Selected = true;
                Gallery.Items[Local.CurrentIndex].Focused = true;
                Gallery.ScrollToIndex(Local.CurrentIndex);
            }
            catch (ArgumentOutOfRangeException) { }
        }
    }


    /// <summary>
    /// View the next image using jump step.
    /// </summary>
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP003:Dispose previous before re-assigning", Justification = "<Pending>")]
    private async Task ViewNextAsync(int step,
        bool resetZoom = true,
        bool isSkipCache = false,
        int? frameIndex = null,
        string? filePath = null,
        CancellationTokenSource? tokenSrc = null)
    {
        Local.ImageTransform.Clear();

        if (Local.Images.Length == 0 && string.IsNullOrEmpty(filePath))
        {
            Local.CurrentIndex = -1;
            Local.Metadata = null;

            LoadImageInfo(ImageInfoUpdateTypes.All);

            return;
        }

        // temp index
        var imageIndex = Local.CurrentIndex + step;
        var oldImgPath = Local.Images.GetFilePath(Local.CurrentIndex);


        try
        {
            // check if loading is cancelled
            tokenSrc?.Token.ThrowIfCancellationRequested();
            IgPhoto? photo = null;

            var readSettings = new CodecReadOptions()
            {
                ColorProfileName = Config.ColorProfile,
                ApplyColorProfileForAll = Config.ShouldUseColorProfileForAll,
                ImageChannel = Local.ImageChannel,
                AutoScaleDownLargeImage = true,
                UseEmbeddedThumbnailRawFormats = Config.UseEmbeddedThumbnailRawFormats,
                UseEmbeddedThumbnailOtherFormats = Config.UseEmbeddedThumbnailOtherFormats,
                EmbeddedThumbnailMinWidth = Config.EmbeddedThumbnailMinWidth,
                EmbeddedThumbnailMinHeight = Config.EmbeddedThumbnailMinHeight,
                FrameIndex = frameIndex,
            };


            // Validate image index
            #region Validate image index

            if (Local.Images.Length > 0)
            {
                // Reach end of list
                if (imageIndex >= Local.Images.Length)
                {
                    _uiReporter.Report(new(new ImageEventArgs()
                    {
                        Index = Local.CurrentIndex,
                        FilePath = oldImgPath,
                    }, nameof(Local.RaiseLastImageReachedEvent)));

                    if (!Config.EnableLoopBackNavigation) return;
                }

                // Reach the first image of list
                if (imageIndex < 0)
                {
                    _uiReporter.Report(new(new ImageEventArgs()
                    {
                        Index = Local.CurrentIndex,
                        FilePath = oldImgPath,
                    }, nameof(Local.RaiseFirstImageReachedEvent)));


                    if (!Config.EnableLoopBackNavigation) return;
                }
            }


            // Check if current index is greater than upper limit
            if (imageIndex >= Local.Images.Length)
                imageIndex = 0;

            // Check if current index is less than lower limit
            if (imageIndex < 0)
                imageIndex = Local.Images.Length - 1;


            // load image metadata
            if (!string.IsNullOrEmpty(filePath))
            {
                photo = new IgPhoto(filePath);
                readSettings.FirstFrameOnly = Config.SinglePageFormats.Contains(photo.Extension);

                if (isSkipCache || Local.Metadata == null
                    || !Local.Metadata.FilePath.Equals(filePath, StringComparison.InvariantCultureIgnoreCase)
                    || Local.Metadata.FrameIndex != frameIndex)
                {
                    Local.Metadata = PhotoCodec.LoadMetadata(filePath, readSettings);
                }
            }
            else
            {
                Local.Metadata = Local.Images.GetMetadata(imageIndex, frameIndex);

                // Update current index
                Local.CurrentIndex = imageIndex;
            }

            #endregion // Validate image index


            // image frame index
            Local.CurrentFrameIndex = Local.Metadata.FrameIndex;

            var imgFilePath = string.IsNullOrEmpty(filePath)
                ? Local.Images.GetFilePath(Local.CurrentIndex)
                : filePath;
            // check if we should use Webview2 viewer
            var useWebview2 = imgFilePath.EndsWith(".svg", StringComparison.InvariantCultureIgnoreCase);


            // set busy state
            Local.IsBusy = true;
            
            _uiReporter.Report(new(new ImageLoadingEventArgs()
            {
                Index = Local.CurrentIndex,
                NewIndex = imageIndex,
                FilePath = imgFilePath,
                FrameIndex = Local.CurrentFrameIndex,
                IsViewingSeparateFrame = frameIndex != null,
                UseWebview2 = useWebview2,
            }, nameof(Local.RaiseImageLoadingEvent)));


            // check if loading is cancelled
            tokenSrc?.Token.ThrowIfCancellationRequested();

            // apply image list settings
            Local.Images.SinglePageFormats = Config.SinglePageFormats;
            Local.Images.ReadOptions = readSettings;


            // if we are using Webview2
            if (useWebview2)
            {
                photo = new IgPhoto(imgFilePath)
                {
                    Metadata = Local.Metadata,
                };
            }
            else
            {

                // directly load the image file, skip image list
                if (photo != null)
                {
                    await photo.LoadAsync(readSettings, tokenSrc);
                }
                else
                {
                    photo = await Local.Images.GetAsync(
                        imageIndex,
                        useCache: !isSkipCache,
                        tokenSrc: tokenSrc
                    );
                }
            }


            // check if loading is cancelled
            tokenSrc?.Token.ThrowIfCancellationRequested();


            _uiReporter.Report(new(new ImageLoadedEventArgs()
            {
                Index = imageIndex,
                FrameIndex = Local.CurrentFrameIndex,
                IsViewingSeparateFrame = frameIndex != null,
                FilePath = imgFilePath,
                Data = photo,
                Error = photo?.Error,
                ResetZoom = resetZoom,
                UseWebview2 = useWebview2,
            }, nameof(Local.RaiseImageLoadedEvent)));

        }
        catch (OperationCanceledException)
        {
            Local.Images.CancelLoading(imageIndex);
            PicMain.DisposeImageResources();

            var imgFilePath = string.IsNullOrEmpty(filePath)
                ? Local.Images.GetFilePath(Local.CurrentIndex)
                : filePath;

            _uiReporter.Report(new(new ImageEventArgs()
            {
                Index = imageIndex,
                FilePath = imgFilePath,
            }, nameof(Local.RaiseImageUnloadedEvent)));
        }

        Local.IsBusy = false;

    }


    /// <summary>
    /// View the next image using jump step
    /// </summary>
    /// <param name="step">The step to change image index. Use <c>0</c> to reload the viewing image.</param>
    /// <param name="resetZoom"></param>
    /// <param name="isSkipCache"></param>
    /// <param name="frameIndex">Use <c>null</c> to load the default frame index.</param>
    /// <param name="filePath">Load this file and ignore the image list.</param>
    public async Task ViewNextCancellableAsync(int step,
        bool resetZoom = true,
        bool isSkipCache = false,
        int? frameIndex = null,
        string? filePath = null)
    {
        _loadCancelTokenSrc?.Cancel();
        _loadCancelTokenSrc?.Dispose();
        _loadCancelTokenSrc = new();

        await ViewNextAsync(step, resetZoom, isSkipCache, frameIndex, filePath, _loadCancelTokenSrc);
    }


    #endregion // Image Loading functions


    // UI reporter
    #region UI reporter

    private void ReportToUIThread(ProgressReporterEventArgs e)
    {
        // Image is being loaded
        if (e.Type.Equals(nameof(Local.RaiseImageLoadingEvent), StringComparison.InvariantCultureIgnoreCase)
            && e.Data is ImageLoadingEventArgs e1)
        {
            Local.RaiseImageLoadingEvent(e1);
            HandleImageProgress_Loading(e1);
            return;
        }

        // Image is loaded
        if (e.Type.Equals(nameof(Local.RaiseImageLoadedEvent), StringComparison.InvariantCultureIgnoreCase)
            && e.Data is ImageLoadedEventArgs e2)
        {
            Local.RaiseImageLoadedEvent(e2);
            _ = HandleImageProgress_LoadedAsync(e2);
            return;
        }

        // Image is unloaded
        if (e.Type.Equals(nameof(Local.RaiseImageUnloadedEvent), StringComparison.InvariantCultureIgnoreCase)
            && e.Data is ImageEventArgs e3)
        {
            Local.RaiseImageUnloadedEvent(e3);
            return;
        }

        // Image list is loaded
        if (e.Type.Equals(nameof(Local.RaiseImageListLoadedEvent), StringComparison.InvariantCultureIgnoreCase)
            && e.Data is ImageListLoadedEventArgs e4)
        {
            Local.RaiseImageListLoadedEvent(e4);
            HandleImageList_Loaded(e4);
            return;
        }

        // the first image is reached
        if (e.Type.Equals(nameof(Local.RaiseFirstImageReachedEvent), StringComparison.InvariantCultureIgnoreCase)
            && e.Data is ImageEventArgs e5)
        {
            Local.RaiseFirstImageReachedEvent(e5);
            HandleImage_FirstReached();
            return;
        }

        // the last image is reached
        if (e.Type.Equals(nameof(Local.RaiseLastImageReachedEvent), StringComparison.InvariantCultureIgnoreCase)
            && e.Data is ImageEventArgs e6)
        {
            Local.RaiseLastImageReachedEvent(e6);
            HandleImage_LastReached();
            return;
        }
    }


    private void HandleImageProgress_Loading(ImageLoadingEventArgs e)
    {
        Local.IsImageError = false;

        PicMain.ClearMessage();
        if (e.Index >= 0 || !string.IsNullOrEmpty(e.FilePath))
        {
            PicMain.ShowMessage(Config.Language[$"{Name}._Loading"], null, delayMs: 1500);
        }

        // Select thumbnail item
        _ = BHelper.RunAsThread(SelectCurrentGalleryThumbnail);

        // show image preview if it's not cached
        if (!Local.Images.IsCached(Local.CurrentIndex))
        {
            ShowImagePreview(e.FilePath, _loadCancelTokenSrc.Token);
        }

        _ = Task.Run(() => LoadImageInfo(ImageInfoUpdateTypes.All, e.FilePath));
    }


    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP007:Don't dispose injected", Justification = "<Pending>")]
    private async Task HandleImageProgress_LoadedAsync(ImageLoadedEventArgs e)
    {
        Exception? error = null;

        // if image needs to display in Webview2 viweer
        if (e.UseWebview2)
        {
            PicMain.ClearMessage();

            try
            {
                await PicMain.SetImageWeb2Async(e.Data, _loadCancelTokenSrc.Token);
            }
            catch (Exception ex) { error = ex; }
        }


        // image error
        if (error != null)
        {
            Local.IsImageError = true;
            Local.ImageModifiedPath = string.Empty;

            IG_Unload();

            var emoji = BHelper.IsOS(WindowsOS.Win11OrLater) ? "🥲" : "🙄";
            var archInfo = Environment.Is64BitProcess ? "64-bit" : "32-bit";
            var appVersion = App.Version + $" ({archInfo}, .NET {Environment.Version})";

            var debugInfo = $"ImageGlass {Constants.APP_CODE.CapitalizeFirst()} v{appVersion}" +
                $"\r\n{ImageMagick.MagickNET.Version}" +
                $"\r\n" +
                $"\r\nℹ️ Error details:" +
                $"\r\n";

            PicMain.ShowMessage(debugInfo +
                error.Source + ": " + error.Message,
                Config.Language[$"{Name}.{nameof(PicMain)}._ErrorText"] + $" {emoji}");
        }

        // use native viewer to display image
        else if (!(e.Data?.ImgData.IsImageNull ?? true))
        {
            // delete clipboard image
            Local.ClipboardImage?.Dispose();
            Local.ClipboardImage = null;
            Local.TempImagePath = null;


            // enable image transition
            var enableFadingTrainsition = false;
            if (Config.EnableImageTransition && !e.IsViewingSeparateFrame)
            {
                var isImageBigForFading = Local.Metadata.Width > 8000
                    || Local.Metadata.Height > 8000;
                enableFadingTrainsition = !_isShowingImagePreview && !isImageBigForFading;
            }

            var resetZoom = (Config.EnableWindowFit && !e.IsViewingSeparateFrame) ? false : e.ResetZoom;

            // set the main image
            PicMain.SetImage(e.Data.ImgData,
                autoAnimate: !e.IsViewingSeparateFrame,
                frameIndex: e.FrameIndex,
                resetZoom: resetZoom,
                enableFading: enableFadingTrainsition);

            // update window fit
            if (e.ResetZoom && Config.EnableWindowFit)
            {
                FitWindowToImage();
            }

            PicMain.ClearMessage();
        }



        if (Local.CurrentIndex >= 0)
        {
            SelectCurrentGalleryThumbnail();
        }


        _isShowingImagePreview = false;
        LoadImageInfo(ImageInfoUpdateTypes.Dimension | ImageInfoUpdateTypes.FrameCount);


        // Collect system garbage
        Local.GcCollect();
    }


    private void HandleImageList_Loaded(ImageListLoadedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.InitFilePath))
        {
            UpdateCurrentIndex(e.InitFilePath);
        }

        LoadImageInfo(ImageInfoUpdateTypes.ListCount);

        // Load thumnbnail
        BHelper.RunAsThread(LoadGallery);
    }


    private void HandleImage_FirstReached()
    {
        if (!Config.EnableLoopBackNavigation)
        {
            PicMain.ShowMessage(Config.Language[$"{Name}._ReachedFirstImage"],
                Config.InAppMessageDuration);
        }
    }


    private void HandleImage_LastReached()
    {
        if (!Config.EnableLoopBackNavigation)
        {
            PicMain.ShowMessage(Config.Language[$"{Name}._ReachedLastLast"],
                Config.InAppMessageDuration);
        }
    }


    private void ImageTransform_Changed(object? sender, EventArgs e)
    {
        const string TOOLBAR_BUTTON_SAVE_TRANSFORMATION = "Btn_SaveImageTransformation";
        using var btnItem = Toolbar.GetItem(TOOLBAR_BUTTON_SAVE_TRANSFORMATION);

        // has changes, show Save button
        if (Local.ImageTransform.HasChanges && btnItem == null)
        {
            var menuAction = nameof(MnuSave);
            var menuText = MnuSave.Text;

            // use Save as button
            if (Local.Metadata?.SupportsWriting == false)
            {
                menuAction = nameof(MnuSaveAs);
                menuText = MnuSaveAs.Text;
            }

            Toolbar.AddItem(new()
            {
                Id = TOOLBAR_BUTTON_SAVE_TRANSFORMATION,
                Image = nameof(Config.Theme.ToolbarIcons.SaveAs),
                OnClick = new(menuAction),
                Alignment = ToolStripItemAlignment.Right,
                Text = menuText,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            }, 1);

            Toolbar.UpdateAlignment();
            LoadImageInfo(ImageInfoUpdateTypes.Path | ImageInfoUpdateTypes.Name);
        }
        // no change, hide button
        else if (!Local.ImageTransform.HasChanges && btnItem != null)
        {
            Toolbar.Items.RemoveByKey(TOOLBAR_BUTTON_SAVE_TRANSFORMATION);

            Toolbar.UpdateAlignment();
            LoadImageInfo(ImageInfoUpdateTypes.Path | ImageInfoUpdateTypes.Name);
        }
    }


    #endregion // UI reporter


    /// <summary>
    /// Show image preview using the thumbnail
    /// </summary>
    public void ShowImagePreview(string filePath, CancellationToken token = default)
    {
        if (InvokeRequired)
        {
            Invoke(ShowImagePreview, filePath, token);
            return;
        }

        if (Local.Metadata == null || !Config.ShowImagePreview) return;
        WicBitmapSource? wicSrc = null;


        try
        {
            token.ThrowIfCancellationRequested();

            var isImageBigForThumbnail = Local.Metadata.Width >= 4000
                || Local.Metadata.Height >= 4000;

            // get embedded thumbnail for preview
            wicSrc = PhotoCodec.GetEmbeddedThumbnail(filePath,
                rawThumbnail: true, exifThumbnail: isImageBigForThumbnail, token: token);

            // use thumbnail image for preview
            if (wicSrc == null && isImageBigForThumbnail)
            {
                if (Local.CurrentIndex >= 0 && Local.CurrentIndex < Gallery.Items.Count)
                {
                    token.ThrowIfCancellationRequested();
                    var thumbnailPath = Gallery.Items[Local.CurrentIndex].FileName;
                    var thumb = Gallery.Items[Local.CurrentIndex].ThumbnailImage;

                    if (thumb != null
                        && thumbnailPath.Equals(filePath, StringComparison.InvariantCultureIgnoreCase))
                    {
                        wicSrc?.Dispose();
                        wicSrc = BHelper.ToWicBitmapSource(thumb);
                    }
                }
            }
        }
        catch (OperationCanceledException) { return; }
        catch { }


        if (wicSrc != null)
        {
            try
            {
                Size previewSize;
                token.ThrowIfCancellationRequested();

                // get preview size
                if (Config.ZoomMode == ZoomMode.LockZoom)
                {
                    previewSize = new(Local.Metadata.Width, Local.Metadata.Height);
                }
                else
                {
                    var zoomFactor = PicMain.CalculateZoomFactor(Config.ZoomMode, Local.Metadata.Width, Local.Metadata.Height, PicMain.Width, PicMain.Height);

                    previewSize = new((int)(Local.Metadata.Width * zoomFactor), (int)(Local.Metadata.Height * zoomFactor));
                }


                // scale the preview image
                if (wicSrc.Width < previewSize.Width || wicSrc.Height < previewSize.Height)
                {
                    // sync interpolation mode for the preview
                    var interpolation = DirectN.WICBitmapInterpolationMode.WICBitmapInterpolationModeLinear;
                    if (PicMain.ZoomFactor > 1 &&
                        (PicMain.CurrentInterpolation == ImageInterpolation.HighQualityBicubic))
                    {
                        interpolation = DirectN.WICBitmapInterpolationMode.WICBitmapInterpolationModeNearestNeighbor;
                    }

                    token.ThrowIfCancellationRequested();
                    wicSrc.Scale(previewSize.Width, previewSize.Height, interpolation);
                }

                token.ThrowIfCancellationRequested();
                PicMain.SetImage(new()
                {
                    Image = wicSrc,
                    CanAnimate = false,
                    FrameCount = 1,
                }, enableFading: Config.EnableImageTransition, isForPreview: true);

                _isShowingImagePreview = true;
            }
            catch (OperationCanceledException) { }
        }
    }


    /// <summary>
    /// Loads image info in status bar
    /// </summary>
    public void LoadImageInfo(ImageInfoUpdateTypes types = ImageInfoUpdateTypes.All,
        string? filename = null)
    {
        if (InvokeRequired)
        {
            Invoke(LoadImageInfo, types, filename);
            return;
        }

        var updateAll = ImageInfo.IsNull || types.HasFlag(ImageInfoUpdateTypes.All);
        var clipboardImageText = string.Empty;


        // AppName
        if (Config.InfoItems.Contains(nameof(ImageInfo.AppName)))
        {
            ImageInfo.AppName = App.AppName;
        }
        else
        {
            ImageInfo.AppName = string.Empty;
        }

        // Zoom
        if (updateAll || types.HasFlag(ImageInfoUpdateTypes.Zoom))
        {
            if (Config.InfoItems.Contains(nameof(ImageInfo.Zoom)))
            {
                ImageInfo.Zoom = $"{Math.Round(PicMain.ZoomFactor * 100, 2)}%";
            }
            else
            {
                ImageInfo.Zoom = string.Empty;
            }
        }


        // the viewing image is a clipboard image
        if (Local.ClipboardImage != null && !Local.ClipboardImage.ComObject.IsDisposed)
        {
            clipboardImageText = Config.Language[$"{Name}._ClipboardImage"];

            // Dimension
            if (updateAll || types.HasFlag(ImageInfoUpdateTypes.Dimension))
            {
                if (Config.InfoItems.Contains(nameof(ImageInfo.Dimension)))
                {
                    ImageInfo.Dimension = $"{Local.ClipboardImage.Width} x {Local.ClipboardImage.Height} px";
                }
                else
                {
                    ImageInfo.Dimension = string.Empty;
                }
            }
        }
        // the viewing image is from the image list
        else
        {
            var fullPath = string.IsNullOrEmpty(filename)
                ? Local.Images.GetFilePath(Local.CurrentIndex)
                : BHelper.ResolvePath(filename);

            // ListCount
            if (updateAll || types.HasFlag(ImageInfoUpdateTypes.ListCount))
            {
                if (Config.InfoItems.Contains(nameof(ImageInfo.ListCount))
                    && Local.Images.Length > 0)
                {
                    var listInfo = new StringBuilder(3);
                    listInfo.Append(Local.CurrentIndex + 1);
                    listInfo.Append('/');
                    listInfo.Append(Local.Images.Length);

                    ImageInfo.ListCount = string.Format(
                        Config.Language[$"_.{nameof(ImageInfo)}._{nameof(ImageInfo.ListCount)}"],
                        listInfo.ToString());
                }
                else
                {
                    ImageInfo.ListCount = string.Empty;
                }
            }

            // Name
            if (updateAll || types.HasFlag(ImageInfoUpdateTypes.Name))
            {
                if (Config.InfoItems.Contains(nameof(ImageInfo.Name)))
                {
                    var askterisk = Local.ImageTransform.HasChanges ? "*" : string.Empty;
                    ImageInfo.Name = Path.GetFileName(fullPath) + askterisk;
                }
                else
                {
                    ImageInfo.Name = string.Empty;
                }
            }

            // Path
            if (updateAll || types.HasFlag(ImageInfoUpdateTypes.Path))
            {
                if (Config.InfoItems.Contains(nameof(ImageInfo.Path)))
                {
                    var askterisk = Local.ImageTransform.HasChanges ? "*" : string.Empty;
                    ImageInfo.Path = fullPath + askterisk;
                }
                else
                {
                    ImageInfo.Path = string.Empty;
                }
            }

            // FileSize
            if (updateAll || types.HasFlag(ImageInfoUpdateTypes.FileSize))
            {
                if (Config.InfoItems.Contains(nameof(ImageInfo.FileSize))
                    && Local.Metadata != null)
                {
                    ImageInfo.FileSize = Local.Metadata.FileSizeFormated;
                }
                else
                {
                    ImageInfo.FileSize = string.Empty;
                }
            }

            // FrameCount
            if (updateAll || types.HasFlag(ImageInfoUpdateTypes.FrameCount))
            {
                if (Config.InfoItems.Contains(nameof(ImageInfo.FrameCount))
                    && Local.Metadata != null
                    && Local.Metadata.FrameCount > 1)
                {
                    var frameInfo = new StringBuilder(3);
                    if (Local.Metadata != null)
                    {
                        frameInfo.Append(Local.Metadata.FrameIndex + 1);
                        frameInfo.Append('/');
                        frameInfo.Append(Local.Metadata.FrameCount);
                    }

                    ImageInfo.FrameCount = string.Format(
                        Config.Language[$"_.{nameof(ImageInfo)}._{nameof(ImageInfo.FrameCount)}"],
                        frameInfo.ToString());
                }
                else
                {
                    ImageInfo.FrameCount = string.Empty;
                }

                // update frame info on PageNav toolbar
                UpdatePageNavToolbarButtonState();
            }

            // Dimension
            if (updateAll || types.HasFlag(ImageInfoUpdateTypes.Dimension))
            {
                if (Config.InfoItems.Contains(nameof(ImageInfo.Dimension))
                    && Local.Metadata != null)
                {
                    ImageInfo.Dimension = $"{Local.Metadata.Width} x {Local.Metadata.Height} px";
                }
                else
                {
                    ImageInfo.Dimension = string.Empty;
                }
            }

            // ModifiedDateTime
            if (updateAll || types.HasFlag(ImageInfoUpdateTypes.ModifiedDateTime))
            {
                if (Config.InfoItems.Contains(nameof(ImageInfo.ModifiedDateTime))
                    && Local.Metadata != null)
                {
                    ImageInfo.ModifiedDateTime = Local.Metadata.FileLastWriteTimeFormated + " (m)";
                }
                else
                {
                    ImageInfo.ModifiedDateTime = string.Empty;
                }
            }

            // ExifRating
            if (updateAll || types.HasFlag(ImageInfoUpdateTypes.ExifRating))
            {
                if (Config.InfoItems.Contains(nameof(ImageInfo.ExifRating))
                    && Local.Metadata != null)
                {
                    ImageInfo.ExifRating = BHelper.FormatStarRatingText(Local.Metadata.ExifRatingPercent);
                }
                else
                {
                    ImageInfo.ExifRating = string.Empty;
                }
            }

            // ExifDateTime
            if (updateAll || types.HasFlag(ImageInfoUpdateTypes.ExifDateTime))
            {
                if (Config.InfoItems.Contains(nameof(ImageInfo.ExifDateTime))
                    && Local.Metadata != null
                    && Local.Metadata.ExifDateTime != null)
                {
                    ImageInfo.ExifDateTime = BHelper.FormatDateTime(Local.Metadata.ExifDateTime) + " (e)";
                }
                else
                {
                    ImageInfo.ExifDateTime = string.Empty;
                }
            }

            // ExifDateTimeOriginal
            if (updateAll || types.HasFlag(ImageInfoUpdateTypes.ExifDateTimeOriginal))
            {
                if (Config.InfoItems.Contains(nameof(ImageInfo.ExifDateTimeOriginal))
                    && Local.Metadata != null
                    && Local.Metadata.ExifDateTimeOriginal != null)
                {
                    ImageInfo.ExifDateTimeOriginal = BHelper.FormatDateTime(Local.Metadata.ExifDateTimeOriginal) + " (o)";
                }
                else
                {
                    ImageInfo.ExifDateTimeOriginal = string.Empty;
                }
            }

            // DateTimeAuto
            if (updateAll || types.HasFlag(ImageInfoUpdateTypes.DateTimeAuto))
            {
                var dtStr = string.Empty;

                if (Config.InfoItems.Contains(nameof(ImageInfo.DateTimeAuto))
                    && Local.Metadata != null)
                {
                    if (Local.Metadata.ExifDateTimeOriginal != null)
                    {
                        dtStr = BHelper.FormatDateTime(Local.Metadata.ExifDateTimeOriginal) + " (o)";
                    }
                    else if (Local.Metadata.ExifDateTime != null)
                    {
                        dtStr = BHelper.FormatDateTime(Local.Metadata.ExifDateTime) + " (e)";
                    }
                    else
                    {
                        dtStr = Local.Metadata.FileLastWriteTimeFormated + " (m)";
                    }
                }

                ImageInfo.DateTimeAuto = dtStr;
            }

            // ColorSpace
            if (updateAll || types.HasFlag(ImageInfoUpdateTypes.ColorSpace))
            {
                if (Config.InfoItems.Contains(nameof(ImageInfo.ColorSpace))
                    && Local.Metadata != null
                    && !string.IsNullOrEmpty(Local.Metadata.ColorSpace))
                {
                    var colorProfile = !string.IsNullOrEmpty(Local.Metadata.ColorProfile)
                        ? Local.Metadata.ColorProfile
                        : "-";

                    if (Local.Metadata.ColorSpace.Equals(colorProfile, StringComparison.InvariantCultureIgnoreCase))
                    {
                        ImageInfo.ColorSpace = Local.Metadata.ColorSpace;
                    }
                    else
                    {
                        ImageInfo.ColorSpace = $"{Local.Metadata.ColorSpace}/{colorProfile}";
                    }
                }
                else
                {
                    ImageInfo.ColorSpace = string.Empty;
                }
            }

        }


        Text = ImageInfo.ToString(Config.InfoItems, Local.ClipboardImage != null, clipboardImageText);
    }


    /// <summary>
    /// Executes user action
    /// </summary>
    public async Task ExecuteUserActionAsync(SingleAction? ac)
    {
        if (ac == null) return;
        if (string.IsNullOrEmpty(ac.Executable)) return;

        var langPath = $"_._UserAction";
        Exception? error = null;


        // Executable is name of main menu item
        #region Main menu item executable
        var mnu = MnuMain.FindMenuItem(ac.Executable);

        if (mnu != null) mnu.PerformClick();
        else if (ac.Executable.StartsWith("Mnu"))
        {
            error = new MissingFieldException(string.Format(Config.Language[$"{langPath}._MenuNotFound"], ac.Executable));
        }
        #endregion


        // Executable is a predefined function in FrmMain.IGMethods
        #region IGMethods executable
        else if (ac.Executable.StartsWith("IG_"))
        {
            // Find the private method in FrmMain
            var method = GetType().GetMethod(ac.Executable);


            // run built-in method
            if (method is not null)
            {
                // check method's params
                var paramItems = method.GetParameters();
                var paramters = new List<object>();

                if (paramItems.Length == 1)
                {
                    object? methodArg = null;
                    var type = Nullable.GetUnderlyingType(paramItems[0].ParameterType) ?? paramItems[0].ParameterType;

                    if (type.IsPrimitive || type.Equals(typeof(string)))
                    {
                        if (string.IsNullOrEmpty(ac.Arguments.ToString()))
                        {
                            methodArg = null;
                        }
                        else
                        {
                            try
                            {
                                methodArg = Convert.ChangeType(ac.Arguments, type);
                            }
                            catch (Exception ex) { error = ex; }
                        }
                    }
                    else
                    {
                        error = new ArgumentException(
                            string.Format(Config.Language[$"{langPath}._MethodArgumentNotSupported"], ac.Executable),
                            nameof(ac.Arguments));
                    }


                    if (methodArg != null && methodArg.GetType().IsArray)
                    {
                        paramters.AddRange((object[])methodArg);
                    }
                    else
                    {
                        paramters.Add(methodArg);
                    }
                }


                // method must be bool/void()
                try
                {
                    method.Invoke(this, paramters.ToArray());
                    error = null;
                }
                catch (Exception ex) { error = ex; }
            }
            else
            {
                error = new MissingMethodException(
                    string.Format(Config.Language[$"{langPath}._MethodNotFound"], ac.Executable));
            }
        }

        #endregion


        // Executable is a free path
        #region Free path executable
        else
        {
            var currentFilePath = Local.Images.GetFilePath(Local.CurrentIndex);
            var procArgs = $"{ac.Arguments}".Replace(Constants.FILE_MACRO, $"\"{currentFilePath}\"");

            var result = await BHelper.RunExeCmd(ac.Executable, procArgs, true);
            if (result != IgExitCode.Done)
            {
                error = new Exception(string.Format(Config.Language[$"{langPath}._Win32ExeError"], ac.Executable));
            }
        }

        #endregion


        // run next action
        if (error == null)
        {
            await ExecuteUserActionAsync(ac.NextAction);
        }
        // show error if any
        else
        {
            Config.ShowError(this, error.ToString(), Config.Language["_._Error"], error.Message);
        }
    }


    /// <summary>
    /// Executes action from mouse event, returns the action executable.
    /// </summary>
    public string? ExecuteMouseAction(MouseClickEvent e)
    {
        if (Config.MouseClickActions.TryGetValue(e, out var toggleAction))
        {
            var isToggleOff = ToggleAction.IsToggleOff(toggleAction.Id);
            var action = isToggleOff
                ? toggleAction.ToggleOff
                : toggleAction.ToggleOn;

            var executable = action?.Executable.Trim();
            if (string.IsNullOrWhiteSpace(executable)) return null;


            if (e == MouseClickEvent.RightClick)
            {
                // update PicMain's context menu
                Local.UpdateFrmMain(UpdateRequests.MouseActions);

                if (executable != nameof(IG_OpenContextMenu)
                    && executable != nameof(IG_OpenMainMenu))
                {
                    _ = ExecuteUserActionAsync(action);
                }
            }
            else
            {
                _ = ExecuteUserActionAsync(action);
            }

            // update toggling value
            ToggleAction.SetToggleValue(toggleAction.Id, !isToggleOff);

            return executable;
        }

        return null;
    }



    #region Main Menu component

    /// <summary>
    /// Shows submenu items
    /// </summary>
    /// <param name="parentMenu"></param>
    private void ShowSubMenu(ToolStripMenuItem parentMenu)
    {
        MnuSubMenu.Items.Clear();

        foreach (ToolStripItem item in parentMenu.DropDownItems)
        {
            if (item.GetType() == typeof(ToolStripSeparator))
            {
                MnuSubMenu.Items.Add(new ToolStripSeparator());
            }
            else
            {
                MnuSubMenu.Items.Add(MenuUtils.Clone((ToolStripMenuItem)item));
            }
        }

        MnuSubMenu.Show(Cursor.Position);
    }

    private void MnuMain_Opening(object sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            // Alert user if there is a new version
            if (Config.ShowNewVersionIndicator)
            {
                MnuCheckForUpdate.Text = MnuCheckForUpdate.Text = Config.Language[$"{Name}.{nameof(MnuCheckForUpdate)}._NewVersion"];
                MnuHelp.BackColor = MnuCheckForUpdate.BackColor = Color.FromArgb(35, 255, 165, 2);
            }
            else
            {
                MnuCheckForUpdate.Text = MnuCheckForUpdate.Text = Config.Language["_._CheckForUpdate"];
                MnuHelp.BackColor = MnuCheckForUpdate.BackColor = Color.Transparent;
            }

            MnuViewChannels.Enabled = true;
            MnuToggleImageAnimation.Enabled =
                MnuViewPreviousFrame.Enabled =
                MnuViewNextFrame.Enabled =
                MnuViewFirstFrame.Enabled =
                MnuViewLastFrame.Enabled = false;

            MnuSetLockScreen.Enabled = true;

            if (Local.Metadata?.FrameCount > 1)
            {
                MnuViewChannels.Enabled = false;

                MnuToggleImageAnimation.Enabled =
                    MnuViewPreviousFrame.Enabled =
                    MnuViewNextFrame.Enabled =
                    MnuViewFirstFrame.Enabled =
                    MnuViewLastFrame.Enabled = true;
            }


            // Get EditApp for editing
            UpdateEditAppInfoForMenu();
        }
        catch { }

    }


    private void MnuContext_Opening(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // clear current items
        MnuContext.Items.Clear();

        var hasClipboardImage = Local.ClipboardImage != null;
        var imageNotFound = !File.Exists(Local.Images.GetFilePath(Local.CurrentIndex));


        // toolbar menu
        MnuContext.Items.Add(MenuUtils.Clone(MnuToggleToolbar));
        MnuContext.Items.Add(MenuUtils.Clone(MnuToggleTopMost));


        // Get Edit App info
        if (!imageNotFound)
        {
            if (!hasClipboardImage)
            {
                MnuContext.Items.Add(new ToolStripSeparator());
                MnuContext.Items.Add(MenuUtils.Clone(MnuLoadingOrders));
            }

            if (!Local.IsImageError
                && !hasClipboardImage
                && Local.Metadata?.FrameCount <= 1)
            {
                MnuContext.Items.Add(MenuUtils.Clone(MnuViewChannels));
            }

            MnuContext.Items.Add(new ToolStripSeparator());
            MnuContext.Items.Add(MenuUtils.Clone(MnuOpenWith));


            // menu Edit
            UpdateEditAppInfoForMenu();
            MnuContext.Items.Add(MenuUtils.Clone(MnuEdit));
        }


        if ((!imageNotFound && !Local.IsImageError) || Local.ClipboardImage != null)
        {
            MnuContext.Items.Add(MenuUtils.Clone(MnuSetDesktopBackground));
            MnuContext.Items.Add(MenuUtils.Clone(MnuSetLockScreen));
        }


        // Menu group: CLIPBOARD
        #region Menu group: CLIPBOARD
        MnuContext.Items.Add(new ToolStripSeparator());//------------

        MnuContext.Items.Add(MenuUtils.Clone(MnuPasteImage));
        MnuContext.Items.Add(MenuUtils.Clone(MnuCopyImageData));

        if (!imageNotFound && Local.ClipboardImage == null)
        {
            MnuContext.Items.Add(MenuUtils.Clone(MnuCopyFile));
            MnuContext.Items.Add(MenuUtils.Clone(MnuCutFile));
        }

        if (!imageNotFound && Local.ClipboardImage == null)
        {
            MnuContext.Items.Add(MenuUtils.Clone(MnuClearClipboard));
        }
        #endregion


        if (!imageNotFound && Local.ClipboardImage == null)
        {
            MnuContext.Items.Add(new ToolStripSeparator());//------------
            MnuContext.Items.Add(MenuUtils.Clone(MnuRename));
            MnuContext.Items.Add(MenuUtils.Clone(MnuMoveToRecycleBin));

            MnuContext.Items.Add(new ToolStripSeparator());//------------
            MnuContext.Items.Add(MenuUtils.Clone(MnuCopyPath));
            MnuContext.Items.Add(MenuUtils.Clone(MnuOpenLocation));
            MnuContext.Items.Add(MenuUtils.Clone(MnuImageProperties));
        }
    }



    // Menu File
    #region Menu File

    private void MnuOpenFile_Click(object sender, EventArgs e)
    {
        IG_OpenFile();
    }

    private void MnuNewWindow_Click(object sender, EventArgs e)
    {
        // TODO
    }

    private void MnuSave_Click(object sender, EventArgs e)
    {
        IG_Save();
    }

    private void MnuSaveAs_Click(object sender, EventArgs e)
    {
        IG_SaveAs();
    }

    private void MnuOpenWith_Click(object sender, EventArgs e)
    {
        IG_OpenWith();
    }

    private void MnuEdit_Click(object sender, EventArgs e)
    {
        IG_OpenEditApp();
    }

    private void MnuPrint_Click(object sender, EventArgs e)
    {
        IG_Print();
    }

    private void MnuShare_Click(object sender, EventArgs e)
    {
        IG_Share();
    }

    private void MnuRefresh_Click(object sender, EventArgs e)
    {
        IG_Refresh();
    }

    private void MnuReload_Click(object sender, EventArgs e)
    {
        IG_Reload();
    }

    private void MnuReloadImageList_Click(object sender, EventArgs e)
    {
        IG_ReloadList();
    }

    private void MnuUnload_Click(object sender, EventArgs e)
    {
        IG_Unload();
    }

    #endregion // Menu File


    // Menu Navigation
    #region Menu Navigation
    private void MnuViewNext_Click(object sender, EventArgs e)
    {
        IG_ViewNextImage();
    }

    private void MnuViewPrevious_Click(object sender, EventArgs e)
    {
        IG_ViewPreviousImage();
    }

    private void MnuGoTo_Click(object sender, EventArgs e)
    {
        IG_GoTo();
    }

    private void MnuGoToFirst_Click(object sender, EventArgs e)
    {
        IG_GoToFirst();
    }

    private void MnuGoToLast_Click(object sender, EventArgs e)
    {
        IG_GoToLast();
    }

    private void MnuViewNextFrame_Click(object sender, EventArgs e)
    {
        IG_ViewNextFrame();
    }

    private void MnuViewPreviousFrame_Click(object sender, EventArgs e)
    {
        IG_ViewPreviousFrame();
    }

    private void MnuViewFirstFrame_Click(object sender, EventArgs e)
    {
        IG_ViewFirstFrame();
    }

    private void MnuViewLastFrame_Click(object sender, EventArgs e)
    {
        IG_ViewLastFrame();
    }

    #endregion // Menu Navigation


    // Menu Zoom
    #region Menu Zoom
    private void MnuZoomIn_Click(object sender, EventArgs e)
    {
        IG_ZoomIn();
    }

    private void MnuZoomOut_Click(object sender, EventArgs e)
    {
        IG_ZoomOut();
    }

    private void MnuCustomZoom_Click(object sender, EventArgs e)
    {
        IG_CustomZoom();
    }

    private void MnuActualSize_Click(object sender, EventArgs e)
    {
        IG_SetZoom(1f);
    }

    private void MnuAutoZoom_Click(object sender, EventArgs e)
    {
        IG_SetZoomMode(nameof(ZoomMode.AutoZoom));
    }

    private void MnuLockZoom_Click(object sender, EventArgs e)
    {
        IG_SetZoomMode(nameof(ZoomMode.LockZoom));
    }

    private void MnuScaleToWidth_Click(object sender, EventArgs e)
    {
        IG_SetZoomMode(nameof(ZoomMode.ScaleToWidth));
    }

    private void MnuScaleToHeight_Click(object sender, EventArgs e)
    {
        IG_SetZoomMode(nameof(ZoomMode.ScaleToHeight));
    }

    private void MnuScaleToFit_Click(object sender, EventArgs e)
    {
        IG_SetZoomMode(nameof(ZoomMode.ScaleToFit));
    }

    private void MnuScaleToFill_Click(object sender, EventArgs e)
    {
        IG_SetZoomMode(nameof(ZoomMode.ScaleToFill));
    }



    #endregion // Menu Zoom


    // Menu Panning
    #region Panning

    private void MnuPanLeft_Click(object sender, EventArgs e)
    {
        IG_PanLeft();
    }

    private void MnuPanRight_Click(object sender, EventArgs e)
    {
        IG_PanRight();
    }

    private void MnuPanUp_Click(object sender, EventArgs e)
    {
        IG_PanUp();
    }

    private void MnuPanDown_Click(object sender, EventArgs e)
    {
        IG_PanDown();
    }

    private void MnuPanToLeftSide_Click(object sender, EventArgs e)
    {
        IG_PanToLeftSide();
    }

    private void MnuPanToRightSide_Click(object sender, EventArgs e)
    {
        IG_PanToRightSide();
    }

    private void MnuPanToTop_Click(object sender, EventArgs e)
    {
        IG_PanToTopSide();
    }

    private void MnuPanToBottom_Click(object sender, EventArgs e)
    {
        IG_PanToBottomSide();
    }

    #endregion // Menu Panning


    // Menu Layout
    #region Menu Layout
    private void MnuToggleToolbar_Click(object sender, EventArgs e)
    {
        IG_ToggleToolbar();
    }

    private void MnuToggleGallery_Click(object sender, EventArgs e)
    {
        IG_ToggleGallery();
    }

    private void MnuToggleCheckerboard_Click(object sender, EventArgs e)
    {
        IG_ToggleCheckerboard();
    }

    private void MnuToggleTopMost_Click(object sender, EventArgs e)
    {
        IG_ToggleTopMost();
    }
    #endregion // Menu Layout


    // Menu Image
    #region Menu Image

    private void MnuRotateLeft_Click(object sender, EventArgs e)
    {
        IG_Rotate(RotateOption.Left);
    }

    private void MnuRotateRight_Click(object sender, EventArgs e)
    {
        IG_Rotate(RotateOption.Right);
    }

    private void MnuFlipHorizontal_Click(object sender, EventArgs e)
    {
        IG_FlipImage(FlipOptions.Horizontal);
    }

    private void MnuFlipVertical_Click(object sender, EventArgs e)
    {
        IG_FlipImage(FlipOptions.Vertical);
    }

    private void MnuRename_Click(object sender, EventArgs e)
    {
        IG_Rename();
    }

    private void MnuMoveToRecycleBin_Click(object sender, EventArgs e)
    {
        IG_Delete(true);
    }

    private void MnuDeleteFromHardDisk_Click(object sender, EventArgs e)
    {
        IG_Delete(false);
    }

    private void MnuToggleImageAnimation_Click(object sender, EventArgs e)
    {
        IG_ToggleImageAnimation();
    }

    private void MnuExportFrames_Click(object sender, EventArgs e)
    {
        IG_ExportImageFrames();
    }

    private void MnuSetDesktopBackground_Click(object sender, EventArgs e)
    {
        IG_SetDesktopBackground();
    }

    private void MnuSetLockScreen_Click(object sender, EventArgs e)
    {
        IG_SetLockScreenBackground();
    }

    private void MnuOpenLocation_Click(object sender, EventArgs e)
    {
        IG_OpenLocation();
    }

    private void MnuImageProperties_Click(object sender, EventArgs e)
    {
        IG_OpenProperties();
    }


    #endregion // Menu Image


    // Window modes menu
    #region Window modes menu
    private void MnuWindowFit_Click(object sender, EventArgs e)
    {
        IG_ToggleWindowFit();
    }

    private void MnuFrameless_Click(object sender, EventArgs e)
    {
        IG_ToggleFrameless();
    }

    private void MnuFullScreen_Click(object sender, EventArgs e)
    {
        IG_ToggleFullScreen();
    }

    private void MnuSlideshow_Click(object sender, EventArgs e)
    {
        IG_ToggleSlideshow();
    }
    #endregion // Window modes menu


    // Menu Clipboard
    #region Menu Clipboard

    private void MnuPasteImage_Click(object sender, EventArgs e)
    {
        IG_PasteImage();
    }

    private void MnuCopyImageData_Click(object sender, EventArgs e)
    {
        IG_CopyImageData();
    }

    private void MnuCopyFile_Click(object sender, EventArgs e)
    {
        IG_CopyFiles();
    }

    private void MnuCutFile_Click(object sender, EventArgs e)
    {
        IG_CutFiles();
    }

    private void MnuCopyPath_Click(object sender, EventArgs e)
    {
        IG_CopyImagePath();
    }

    private void MnuClearClipboard_Click(object sender, EventArgs e)
    {
        IG_ClearClipboard();
    }


    #endregion // Menu Clipboard


    // Menu Tools
    #region Menu Tools

    private void MnuColorPicker_Click(object sender, EventArgs e)
    {
        IG_ToggleColorPicker();
    }

    private void MnuCropTool_Click(object sender, EventArgs e)
    {
        IG_ToggleCropTool();
    }

    private void MnuPageNav_Click(object sender, EventArgs e)
    {
        IG_TogglePageNavTool();
    }

    private void MnuGetMoreTools_Click(object sender, EventArgs e)
    {
        BHelper.OpenUrl("https://imageglass.org/tools", "app_get_more_tools");
    }


    #endregion // Menu Tools


    // Menu Help
    #region Menu Help
    private void MnuAbout_Click(object sender, EventArgs e)
    {
        IG_About();
    }

    private void MnuCheckForUpdate_Click(object sender, EventArgs e)
    {
        IG_CheckForUpdate(true);
    }

    private void MnuReportIssue_Click(object sender, EventArgs e)
    {
        IG_ReportIssue();
    }

    private void MnuFirstLaunch_Click(object sender, EventArgs e)
    {

    }

    private void MnuSetDefaultPhotoViewer_Click(object sender, EventArgs e)
    {
        IG_SetDefaultPhotoViewer();
    }

    private void MnuUnsetDefaultPhotoViewer_Click(object sender, EventArgs e)
    {
        IG_UnsetDefaultPhotoViewer();
    }

    #endregion // Menu Help


    // Others
    #region Other menu
    private void MnuSettings_Click(object sender, EventArgs e)
    {
        IG_OpenSettings();
    }

    private void MnuExit_Click(object sender, EventArgs e)
    {
        IG_Exit();
    }


    #endregion // Other menu

    #endregion // Main Menu component


}
