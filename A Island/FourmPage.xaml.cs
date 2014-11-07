using A_Island.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

// “拆分页”项模板在 http://go.microsoft.com/fwlink/?LinkId=234234 上有介绍

namespace A_Island
{
    /// <summary>
    /// 显示组标题、组内各项的列表以及当前选定项的
    /// 详细信息的页。
    /// </summary>
    public sealed partial class ForumPage : Page, IDisposable
    {
        private NavigationHelper navigationHelper;
        private ObservableDictionary defaultViewModel = new ObservableDictionary();
        private StorageFile selectedFile;
        //private ThreadSource forum_source = new ThreadSource(), thread_source = new ThreadSource();
        private ForumList forum_list = new ForumList();
        private AcFunViewModel vm_forum = new AcFunViewModel(), vm_thread = new AcFunViewModel();
        private bool isItemClicked = false, isPost = false, isCommentClicked = false;
        private uint clickedThread, selectedPost;
        private List<WriteableBitmap> gifFrames = new List<WriteableBitmap>();
        private bool isGIF = false;

        private const uint LOAD = 100;
        private const uint REFRESH = 50;
        private const float SCALE_RATE = 0.2f;

        /// <summary>
        /// 可将其更改为强类型视图模型。
        /// </summary>
        public ObservableDictionary DefaultViewModel
        {
            get { return this.defaultViewModel; }
        }

        /// <summary>
        /// NavigationHelper 在每页上用于协助导航和
        /// 进程生命期管理
        /// </summary>
        public NavigationHelper NavigationHelper
        {
            get { return this.navigationHelper; }
        }

        public ForumPage()
        {
            this.InitializeComponent();

            // 设置导航帮助程序
            this.navigationHelper = new NavigationHelper(this);
            this.navigationHelper.LoadState += navigationHelper_LoadState;
            this.navigationHelper.SaveState += navigationHelper_SaveState;

            // 设置逻辑页面导航组件，使
            // 页面可一次仅显示一个窗格。
            this.navigationHelper.GoBackCommand = new A_Island.Common.RelayCommand(() => this.GoBack(), () => this.CanGoBack());

            // 开始侦听 Window 大小更改 
            // 以从显示两个窗格变为显示一个窗格
            Window.Current.SizeChanged += Window_SizeChanged;
            this.InvalidateVisualState();
            this.Loaded += ForumPage_Loaded;
            itemListView.Loaded += itemListView_Loaded;
            itemDetail.LayoutUpdated += itemDetail_LayoutUpdated;
            vm_thread.canRefresh = true;
            vm_thread.hasLoadedMore = false;
            vm_forum.canRefresh = true;
            vm_forum.hasLoadedMore = false;

            popupImage.RenderTransform = new CompositeTransform();
            popupImage.ManipulationMode = Windows.UI.Xaml.Input.ManipulationModes.Scale | Windows.UI.Xaml.Input.ManipulationModes.TranslateX | Windows.UI.Xaml.Input.ManipulationModes.TranslateY;
        }

        void itemDetail_LayoutUpdated(object sender, object e)
        {
            var scrollBar = GetVisualChild<ScrollBar>(itemDetail, "VerticalScrollBar");
            if (scrollBar != null)
            {
                scrollBar.ValueChanged += threadScrollBar_ValueChanged;
                itemDetail.LayoutUpdated -= itemDetail_LayoutUpdated;
            }
        }

        private void threadScrollBar_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            ScrollBar scrollbar = sender as ScrollBar;
            if (e.NewValue > e.OldValue && e.NewValue > scrollbar.Maximum - LOAD && !vm_thread.hasLoadedMore)
            {
                vm_thread.hasLoadedMore = true;
#if DEBUG
                Debug.WriteLine("Load");
#endif
                vm_thread.loadMore();
            }
            if (e.NewValue < e.OldValue && e.NewValue < scrollbar.Minimum + REFRESH && vm_thread.canRefresh)
            {
                vm_thread.canRefresh = false;
#if DEBUG
                Debug.WriteLine("Refresh");
#endif
                vm_thread.changeThread(vm_thread.ThreadID);
            }
            if (e.NewValue > scrollbar.Minimum + REFRESH)
            {
                vm_thread.canRefresh = true;
            }
            if (e.NewValue < scrollbar.Maximum - LOAD)
            {
                vm_thread.hasLoadedMore = false;
            }
        }

        private void itemListView_Loaded(object sender, RoutedEventArgs e)
        {
            var scrollBar = GetVisualChild<ScrollBar>(itemListView, "VerticalScrollBar");
            if (scrollBar != null)
                scrollBar.ValueChanged += forumScrollBar_ValueChanged;
        }

        private void forumScrollBar_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            ScrollBar scrollbar = sender as ScrollBar;
            if (e.NewValue > e.OldValue && e.NewValue > scrollbar.Maximum - LOAD && !vm_forum.hasLoadedMore)
            {
                vm_forum.hasLoadedMore = true;
#if DEBUG
                Debug.WriteLine("Load");
#endif
                vm_forum.loadMore();
            }
            if (e.NewValue < e.OldValue && e.NewValue < scrollbar.Minimum + REFRESH && vm_forum.canRefresh)
            {
                vm_forum.canRefresh = false;
#if DEBUG
                Debug.WriteLine("Refresh");
#endif
                vm_forum.changeForum(vm_forum.ForumName);
            }
            if (e.NewValue > scrollbar.Minimum + REFRESH)
            {
                vm_forum.canRefresh = true;
            }
            if (e.NewValue < scrollbar.Maximum - LOAD)
            {
                vm_forum.hasLoadedMore = false;
            }
        }

        void ForumPage_Loaded(object sender, RoutedEventArgs e)
        {
            pageTitle.ItemsSource = forum_list;
            itemListView.ItemsSource = vm_forum.Source;
            itemListView.DataContext = vm_forum;
            itemDetail.ItemsSource = vm_thread.Source;
            itemDetail.DataContext = vm_thread;
            pageTitle.SelectionChanged += pageTitle_SelectionChanged;
            pageTitle.SelectedIndex = 0;
        }

        /// <summary>
        /// 使用在导航过程中传递的内容填充页。  在从以前的会话
        /// 重新创建页时，也会提供任何已保存状态。
        /// </summary>
        /// <param name="sender">
        /// 事件的来源; 通常为 <see cref="NavigationHelper"/>
        /// </param>
        /// <param name="e">事件数据，其中既提供在最初请求此页时传递给
        /// <see cref="Frame.Navigate(Type, Object)"/> 的导航参数，又提供
        /// 此页在以前会话期间保留的状态的
        /// 字典。 首次访问页面时，该状态将为 null。</param>
        private void navigationHelper_LoadState(object sender, LoadStateEventArgs e)
        {

        }

        /// <summary>
        /// 保留与此页关联的状态，以防挂起应用程序或
        /// 从导航缓存中放弃此页。  值必须符合
        /// <see cref="SuspensionManager.SessionState"/> 的序列化要求。
        /// </summary>
        ///<param name="sender">事件的来源；通常为 <see cref="NavigationHelper"/></param>
        ///<param name="e">提供要使用可序列化状态填充的空字典
        ///的事件数据。</param>
        private void navigationHelper_SaveState(object sender, SaveStateEventArgs e)
        {

        }

        private void itemListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (this.UsingLogicalPageNavigation())
            {
                this.navigationHelper.GoBackCommand.RaiseCanExecuteChanged();
                itemDetail.Visibility = Visibility.Visible;
                itemListView.Visibility = Visibility.Collapsed;
            }
            isItemClicked = true;
            if (imgCover.Visibility == Visibility.Visible)
            {
                imgCover.Visibility = Visibility.Collapsed;
            }
            if (itemDetail.Visibility == Visibility.Collapsed)
            {
                itemDetail.Visibility = Visibility.Visible;
            }
            if (options.Visibility == Visibility.Collapsed)
            {
                options.Visibility = Visibility.Visible;
            }
            clickedThread = (e.ClickedItem as Thread).ID;
            vm_thread.changeThread(clickedThread);
            selectedPost = 0;
            this.InvalidateVisualState();
#if DEBUG
            Debug.WriteLine("Click ID: " + vm_thread.ThreadID);
#endif
        }


        private void itemDetail_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("Added: " + e.AddedItems.Count + ", Removed: " + e.RemovedItems.Count);
#endif
            if (e.AddedItems.Count > 0)
            {
                selectedPost = (e.AddedItems[0] as Thread).ID;
            }
            else if (e.RemovedItems.Count > 0)
            {
                selectedPost = 0;
            }
        }

        private T GetVisualChild<T>(DependencyObject parent, string name)
            where T : FrameworkElement
        {
            T child = default(T);
            int numVisuals = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < numVisuals; i++)
            {
                DependencyObject obj = VisualTreeHelper.GetChild(parent, i);
                child = obj as T;
                if (child == null || child.Name != name)
                    child = GetVisualChild<T>(obj, name);
                if (child != null)
                    break;
            }
            return child;
        }

        private void pageTitle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var select = ((sender as ComboBox).SelectedItem as Item).Name;
            vm_forum.changeForum(select);
            forum_list.onClick(select);
            vm_thread.Source.Clear();
            if (imgCover != null)
            {
                if (imgCover.Visibility == Visibility.Collapsed)
                {
                    imgCover.Visibility = Visibility.Visible;
                }
            }
            if (itemDetail != null && itemDetail.Visibility == Visibility.Visible)
            {
                itemDetail.Visibility = Visibility.Collapsed;
            }
            if (itemListView != null && itemListView.Visibility == Visibility.Collapsed)
            {
                itemListView.Visibility = Visibility.Visible;
            }
            if (options != null && options.Visibility == Visibility.Visible)
            {
                options.Visibility = Visibility.Collapsed;
            }
            if (isItemClicked)
            {
                isItemClicked = false;
                this.InvalidateVisualState();
            }
        }

        private async void Image_Tapped(object sender, TappedRoutedEventArgs e)
        {
            isGIF = false;
            if (GIF.GetCurrentState() == ClockState.Active)
            {
                GIF.Stop();
                gifFrames.Clear();
            }
            string imageUrl = ((sender as Image).DataContext as Thread).Image;
            Uri imageUri = new Uri(imageUrl);
            if (imageUrl.EndsWith(".gif"))
            {
                isGIF = true;
                var rass = RandomAccessStreamReference.CreateFromUri(imageUri);
                IRandomAccessStream stream = await rass.OpenReadAsync();
                BitmapDecoder gif = await BitmapDecoder.CreateAsync(stream);
                uint frameCount = gif.FrameCount;
                BitmapFrame frame = await gif.GetFrameAsync(0);
                var frameProperties = await frame.BitmapProperties.GetPropertiesAsync(new List<string>());

                var gifControlExtensionProperties = await (frameProperties["/grctlext"].Value as BitmapPropertiesView).GetPropertiesAsync(new List<string>() { "/Delay" });
                TimeSpan delay = TimeSpan.FromSeconds(Double.Parse(gifControlExtensionProperties["/Delay"].Value.ToString()) / 100);
                BitmapTransform transform = new BitmapTransform()
                {
                    ScaledWidth = gif.OrientedPixelWidth,
                    ScaledHeight = gif.OrientedPixelHeight
                };
                for (uint i = 0; i < frameCount; i++)
                {
                    frame = await gif.GetFrameAsync(i);
                    PixelDataProvider pixelData = await frame.GetPixelDataAsync(BitmapPixelFormat.Bgra8, gif.BitmapAlphaMode, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
                    byte[] sourcePixel = pixelData.DetachPixelData();
                    WriteableBitmap gif_frame = new WriteableBitmap((int)gif.OrientedPixelWidth, (int)gif.OrientedPixelHeight);
                    using (Stream s = gif_frame.PixelBuffer.AsStream())
                    {
                        await s.WriteAsync(sourcePixel, 0, sourcePixel.Length);
                    }
                    gifFrames.Add(gif_frame);
                }
                if (GIF.Children.Count > 0)
                {
                    GIF.Children.Clear();
                }
                var anim = new ObjectAnimationUsingKeyFrames();
                anim.BeginTime = TimeSpan.FromSeconds(0);
                var ts = new TimeSpan();
                for (int frameIndex = 0; frameIndex < gifFrames.Count; frameIndex++)
                {
                    var keyFrame = new DiscreteObjectKeyFrame();
                    keyFrame.KeyTime = KeyTime.FromTimeSpan(ts);
                    keyFrame.Value = gifFrames[frameIndex];
                    ts = ts.Add(delay);
                    anim.KeyFrames.Add(keyFrame);
                }
                Storyboard.SetTarget(anim, popupImage);
                Storyboard.SetTargetProperty(anim, "Source");
                GIF.Children.Add(anim);
                GIF.Begin();
                popupImage.Width = gif.OrientedPixelWidth;
                popupImage.Height = gif.OrientedPixelHeight;
            }
            else
            {
                BitmapImage image = new BitmapImage(imageUri);
                popupImage.Source = image;
                popupImage.Width = Window.Current.CoreWindow.Bounds.Width / 2;
                popupImage.Height = Window.Current.CoreWindow.Bounds.Height / 2;
            }
            System.GC.Collect();
            var ct = (CompositeTransform)popupImage.RenderTransform;
            ct.ScaleX = 1.0f;
            ct.ScaleY = 1.0f;
            ct.TranslateX = 0.0f;
            ct.TranslateY = 0.0f;
            imagePopup.HorizontalOffset = (Window.Current.CoreWindow.Bounds.Width - popupImage.Width) / 2;
            imagePopup.VerticalOffset = (Window.Current.CoreWindow.Bounds.Height - popupImage.Height) / 2;
            imagePopup.IsOpen = true;
        }

        private void image_PointerWheelChanged(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var ct = (CompositeTransform)(sender as Image).RenderTransform;
            if (!isGIF && e.GetCurrentPoint(sender as UIElement).Properties.MouseWheelDelta > 0)
            {
                ct.ScaleX += SCALE_RATE;
                ct.ScaleY += SCALE_RATE;
            }
            else
            {
                if (!isGIF && ct.ScaleX - SCALE_RATE > 0.2 && ct.ScaleY - SCALE_RATE > 0.2)
                {
                    ct.ScaleX -= SCALE_RATE;
                    ct.ScaleY -= SCALE_RATE;
                }
            }
        }

        private void image_ManipulationDelta(object sender, Windows.UI.Xaml.Input.ManipulationDeltaRoutedEventArgs e)
        {
            var ct = (CompositeTransform)(sender as Image).RenderTransform;
            ct.ScaleX *= e.Delta.Scale;
            ct.ScaleY *= e.Delta.Scale;
            ct.TranslateX += e.Delta.Translation.X;
            ct.TranslateY += e.Delta.Translation.Y;
        }

        #region 逻辑页导航

        // 设计了拆分页，以便 Window 具有足够的空间同时显示
        // 列表和详细信息，一次将仅显示一个窗格。
        //
        // 这完全通过一个可表示两个逻辑页的单一物理页
        // 实现。  使用下面的代码可以实现此目标，且用户不会察觉到
        // 区别。

        private const int MinimumWidthForSupportingTwoPanes = 1080;

        /// <summary>
        /// 在确定该页是应用作一个逻辑页还是两个逻辑页时进行调用。
        /// </summary>
        /// <returns>如果窗口应显示充当一个逻辑页，则为 True，
        ///为 false。</returns>
        private bool UsingLogicalPageNavigation()
        {
            return Window.Current.Bounds.Width < MinimumWidthForSupportingTwoPanes;
        }

        /// <summary>
        /// 在 Window 改变大小时调用
        /// </summary>
        /// <param name="sender">当前的 Window</param>
        /// <param name="e">描述 Window 新大小的事件数据</param>
        private void Window_SizeChanged(object sender, Windows.UI.Core.WindowSizeChangedEventArgs e)
        {
            this.InvalidateVisualState();
            var panel = (Grid)commentPopup.Child;
            panel.Width = Window.Current.CoreWindow.Bounds.Width - 50;
            panel.Height = Window.Current.CoreWindow.Bounds.Height - 200;
            commentPopup.HorizontalOffset = (Window.Current.CoreWindow.Bounds.Width - panel.Width) / 2;
            commentPopup.VerticalOffset = (Window.Current.CoreWindow.Bounds.Height - panel.Height) / 2;
        }

        /// <summary>
        /// 在选定列表中的项时进行调用。
        /// </summary>
        /// <param name="sender">显示所选项的 GridView。</param>
        /// <param name="e">描述如何更改选择内容的事件数据。</param>
        private void ItemListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 使视图状态在逻辑页导航起作用时无效，因为
            // 选择内容方面的更改可能会导致当前逻辑页发生相应的更改。
            // 选定某项后，这将会导致从显示项列表
            // 更改为显示选定项的详细信息。  清除选择时，将产生
            // 相反的效果。
            if (this.UsingLogicalPageNavigation()) this.InvalidateVisualState();
        }

        private bool CanGoBack()
        {
            if (this.UsingLogicalPageNavigation() && isItemClicked)
            {
                return true;
            }
            else
            {
                return this.navigationHelper.CanGoBack();
            }
        }
        private void GoBack()
        {
            if (this.UsingLogicalPageNavigation() && isItemClicked)
            {
                // 如果逻辑页导航起作用且存在选定项，则当前将显示
                // 选定项的详细信息。  清除选择后将返回到
                // 项列表。  从用户的角度来看，这是一个逻辑后向
                // 导航。
                isItemClicked = false;
                this.InvalidateVisualState();
                if (itemDetail != null && itemDetail.Visibility == Visibility.Visible)
                {
                    itemDetail.Visibility = Visibility.Collapsed;
                }
                if (itemListView != null && itemListView.Visibility == Visibility.Collapsed)
                {
                    itemListView.Visibility = Visibility.Visible;
                }
            }
            else
            {
                this.navigationHelper.GoBack();
            }
        }

        private void InvalidateVisualState()
        {
            var visualState = DetermineVisualState();
            VisualStateManager.GoToState(this, visualState, false);
            this.navigationHelper.GoBackCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 在确定对应于应用程序视图状态的可视状态的名称时进行
        /// 调用。
        /// </summary>
        /// <returns>所需的可视状态的名称。  此名称与视图状态的名称相同，
        /// 但在纵向和对齐视图中存在选定项时例外，在纵向和对齐视图中，
        /// 此附加逻辑页通过添加 _Detail 后缀表示。</returns>
        private string DetermineVisualState()
        {
            if (!UsingLogicalPageNavigation())
                return "PrimaryView";

            // 在视图状态更改时更新后退按钮的启用状态
            var logicalPageBack = this.UsingLogicalPageNavigation() && isItemClicked;

            return logicalPageBack ? "SinglePane_Detail" : "SinglePane";
        }

        #endregion

        #region NavigationHelper 注册

        /// 此部分中提供的方法只是用于使
        /// NavigationHelper 可响应页面的导航方法。
        /// 
        /// 应将页面特有的逻辑放入用于
        /// <see cref="GridCS.Common.NavigationHelper.LoadState"/>
        /// 和 <see cref="GridCS.Common.NavigationHelper.SaveState"/> 的事件处理程序中。
        /// 除了在会话期间保留的页面状态之外
        /// LoadState 方法中还提供导航参数。

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            navigationHelper.OnNavigatedTo(e);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            navigationHelper.OnNavigatedFrom(e);
        }

        #endregion

        public void Dispose()
        {
            forum_list.Dispose();
            //forum_source.Dispose();
            //thread_source.Dispose();
            //forum_source = null;
            //thread_source = null;
        }

        private void refreshButton_Click(object sender, RoutedEventArgs e)
        {
            vm_thread.changeThread(vm_thread.ThreadID);
        }

        private void postButton_Click(object sender, RoutedEventArgs e)
        {
            var panel = (Grid)commentPopup.Child;
            panel.Width = Window.Current.CoreWindow.Bounds.Width - 50;
            panel.Height = Window.Current.CoreWindow.Bounds.Height - 200;
            commentPopup.HorizontalOffset = (Window.Current.CoreWindow.Bounds.Width - panel.Width) / 2;
            commentPopup.VerticalOffset = (Window.Current.CoreWindow.Bounds.Height - panel.Height) / 2;
            commentPopup.IsOpen = true;
            isPost = true;
            isCommentClicked = true;
        }

        private void replyButton_Click(object sender, RoutedEventArgs e)
        {
            var panel = (Grid)commentPopup.Child;
            panel.Width = Window.Current.CoreWindow.Bounds.Width - 50;
            panel.Height = Window.Current.CoreWindow.Bounds.Height - 200;
            commentPopup.HorizontalOffset = (Window.Current.CoreWindow.Bounds.Width - panel.Width) / 2;
            commentPopup.VerticalOffset = (Window.Current.CoreWindow.Bounds.Height - panel.Height) / 2;
            commentPopup.IsOpen = true;
            isPost = false;
            isCommentClicked = true;
        }

        private void favButton_Click(object sender, RoutedEventArgs e)
        {

        }

        private void cancleButton_Click(object sender, RoutedEventArgs e)
        {
            commentPopup.IsOpen = false;
        }

        private void sendButton_Click(object sender, RoutedEventArgs e)
        {
            if (isCommentClicked)
            {
                if (isPost)
                {
                    //thread_source.postThread(commentContent.Text, commentName.Text, commentEmail.Text, commentTitle.Text, selectedFile);
                }
                else
                {
                    //thread_source.postReply(clickedThread, commentContent.Text, selectedPost, commentName.Text, commentEmail.Text, commentTitle.Text, selectedFile);
                }
            }
            isCommentClicked = false;
            commentPopup.IsOpen = false;
        }

        private void saveButton_Click(object sender, RoutedEventArgs e)
        {

        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = (ComboBox)sender;
            if (item.SelectedIndex > 0)
            {
                String value = (item.SelectedItem as ComboBoxItem).Content as String;
                int index = commentContent.SelectionStart;
                commentContent.Text = commentContent.Text.Insert(index, value);
                commentContent.SelectionStart = index + value.Length;
            }
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            var filePicker = new FileOpenPicker();
            filePicker.FileTypeFilter.Add(".jpg");
            filePicker.FileTypeFilter.Add(".jpeg");
            filePicker.FileTypeFilter.Add(".png");
            filePicker.FileTypeFilter.Add(".gif");
            filePicker.ViewMode = PickerViewMode.Thumbnail;
            filePicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            filePicker.SettingsIdentifier = "PicturePicker";
            filePicker.CommitButtonText = "选择文件";

            selectedFile = await filePicker.PickSingleFileAsync();
            if (selectedFile != null)
            {
                commentImageName.Text = selectedFile.Name;
            }
            else
            {
                commentImageName.Text = "未选择文件";
            }
        }

    }
}
