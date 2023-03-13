using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AntDesign.Internal;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace AntDesign
{
    public class MentionsItem
    {
        public string Value { get; set; }
        public RenderFragment Label { get; set; }
    }
    public partial class Mentions
    {
        [Parameter] public RenderFragment ChildContent { get; set; }
        [Parameter] public bool Disable { get; set; }
        [Parameter] public uint Rows { get; set; } = 3;
        [Parameter] public bool Focused { get; set; }
        [Parameter] public bool Readonly { get; set; }
        [Parameter] public bool Loading { get; set; }

        [Parameter] public Dictionary<string, object> Attributes { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Parameter] public string Placeholder { get; set; }
        [Parameter] public string Value { get; set; } = String.Empty;
        [Parameter] public EventCallback<string> ValueChanged { get; set; }

        [Parameter] public RenderFragment<MentionsTextareaTemplateOptions> TextareaTemplate { get; set; }

        private IEnumerable<MentionsItem> _previousDataSource;

        private IEnumerable<MentionsItem> _dataSource;

        [Parameter]
        public IEnumerable<MentionsItem> DataSource
        {
            get
            {
                return _dataSource;
            }
            set
            {
                _dataSource = value;
                OptionsWorkingSet = _dataSource is null
                    ? OriginalOptions.Select(x => new MentionsItem() { Value = x.Value, Label = x.ChildContent })
                    : _dataSource;
            }
        }
        [Parameter] public EventCallback<string> OnSearch { get; set; }

        internal List<MentionsOption> OriginalOptions { get; set; } = new List<MentionsOption>();
        private OverlayTrigger _overlayTrigger;
        private Task _searchTask;

        internal string ActiveOptionValue { get; set; }
        internal int ActiveOptionIndex => OptionsWorkingSetVisible.FindIndex(x => x.Value == ActiveOptionValue);
        internal bool LoadingOptions { get; private set; }

        /// <summary>
        /// "working set" of options. This is to allow the internal workings of the class 
        /// to only reference one datasource but provide multiple options to consumers of the component.
        /// </summary>
        private IEnumerable<MentionsItem> OptionsWorkingSet { get; set; }

        /// <summary>
        /// Visible options from the "working set"
        /// </summary>
        private List<MentionsItem> OptionsWorkingSetVisible { get; set; } = new List<MentionsItem>();

        private void SetClassMap()
        {
            var prefixCls = "ant-mentions";
            this.ClassMapper.Clear()
                .Add(prefixCls)
                .If($"{prefixCls}-disable", () => this.Disable)
                .If($"{prefixCls}-focused", () => this.Focused)
                .If($"{prefixCls}-rtl", () => RTL)
                ;
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            SetClassMap();
        }

        internal void AddOption(MentionsOption option)
        {
            if (option == null) return;
            var opt = OriginalOptions.Find(x => x.Value == option.Value);
            if (opt == null)
            {
                OriginalOptions.Add(option);
            }
        }
        public List<string> GetMentionNames()
        {
            var r = new List<string>();
            var regex = new System.Text.RegularExpressions.Regex("@([^@\\s]+)\\s");
            regex.Matches(Value).ToList().ForEach(m =>
            {
                var name = m.Groups[1].Value;
                r.Add(name);
            });
            return r;
        }
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                OptionsWorkingSetVisible.Clear();
                OptionsWorkingSetVisible.AddRange(OptionsWorkingSet);
                await JsInvokeAsync(JSInteropConstants.SetEditorKeyHandler, DotNetObjectReference.Create(this), _overlayTrigger.RefBack.Current);
            }
            await base.OnAfterRenderAsync(firstRender);
        }

        protected async override Task OnParametersSetAsync()
        {
            if (DataSource is not null && ChildContent is not null)
            {
                throw new Exception("Cannot set DataSource and ChildContent at the same time - choose one.");
            }

            if (_searchTask is not null && _searchTask.IsCompleted)
            {
                LoadingOptions = false;
                _previousDataSource = DataSource;
                _searchTask = null;
                await ShowOverlay(true, true);
            }

            await base.OnParametersSetAsync();
        }

        [JSInvokable]
        public void PrevOption()
        {
            var index = Math.Max(0, ActiveOptionIndex - 1);
            ActiveOptionValue = OptionsWorkingSetVisible[index].Value;
            StateHasChanged();
        }
        [JSInvokable]
        public void NextOption()
        {
            var index = Math.Min(ActiveOptionIndex + 1, OptionsWorkingSetVisible.Count - 1);
            ActiveOptionValue = OptionsWorkingSetVisible[index].Value;
            StateHasChanged();
        }
        [JSInvokable]
        public async Task EnterOption()
        {
            await ItemClick(ActiveOptionValue);
        }

        async Task HideOverlay()
        {
            await JS.InvokeAsync<double[]>(JSInteropConstants.SetPopShowFlag, false);
            await _overlayTrigger.Hide();
        }

        async Task ShowOverlay(bool resetOptions, bool reCalcPosition)
        {
            await JS.InvokeAsync<double[]>(JSInteropConstants.SetPopShowFlag, true);
            if (resetOptions)
            {
                OptionsWorkingSetVisible.Clear();
                OptionsWorkingSetVisible.AddRange(OptionsWorkingSet);
            }
            if (OptionsWorkingSetVisible.Any())
            {
                ActiveOptionValue = OptionsWorkingSetVisible.First().Value;
            }
            if (reCalcPosition)
            {
                var pos = await JS.InvokeAsync<double[]>(JSInteropConstants.GetCursorXY, _overlayTrigger.RefBack.Current);
                var x = (int)Math.Round(pos[0]);
                var y = (int)Math.Round(pos[1]);
                await _overlayTrigger.Show(x, y);
            }
            else
            {
                await _overlayTrigger.Show();
            }
            await InvokeStateHasChangedAsync();
        }
        async void OnKeyDown(KeyboardEventArgs args)
        {   //↑、↓、回车键只能放进js里判断，不然在Sever异步模式下无法拦截原键功能
            //开启浮窗的判断放在oninput里，不然会有问题
            if (args.Key == "Escape") await HideOverlay();
        }

        async Task OnInput(ChangeEventArgs args)
        {
            Value = args.Value.ToString();
            await ValueChanged.InvokeAsync(Value);

            if (Value.EndsWith("@"))
            {
                await ShowOverlay(true, true);
                return;
            }

            var focusPosition = await JS.InvokeAsync<int>(JSInteropConstants.GetProp, _overlayTrigger.Ref, "selectionStart");
            if (focusPosition == 0)
            {
                await HideOverlay();
                return;
            }

            var v = Value.Substring(0, focusPosition);  //从光标处切断,向前找匹配项
            var lastIndex = v.LastIndexOf("@");
            if (lastIndex >= 0)
            {
                var lastOption = v.Substring(lastIndex + 1);

                await SearchMention(lastOption);
            }
        }

        internal async Task SearchMention(string search)
        {
            if (OnSearch.HasDelegate)
            {
                LoadingOptions = true;
                OptionsWorkingSet = Enumerable.Empty<MentionsItem>();
                await ShowOverlay(true, true);
                _searchTask = OnSearch.InvokeAsync(search);
                await InvokeStateHasChangedAsync();
            }
            else
            {
                OptionsWorkingSetVisible.Clear();
                OptionsWorkingSetVisible.AddRange(OptionsWorkingSet.Where(x => x.Value.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList());
                if (OptionsWorkingSetVisible.Count > 0)
                {
                    await ShowOverlay(false, true);
                }
                else
                {
                    await HideOverlay();
                }
            }
        }

        internal async Task ItemClick(string optionValue)
        {
            var focusPosition = await JS.InvokeAsync<int>(JSInteropConstants.GetProp, _overlayTrigger.Ref, "selectionStart");
            var preText = Value.Substring(0, focusPosition);
            preText = preText.LastIndexOf("@") >= 0 ? Value.Substring(0, preText.LastIndexOf("@")) : preText;
            if (preText.EndsWith(' ')) preText = preText.Substring(0, preText.Length - 1);
            var nextText = Value.Substring(focusPosition);
            if (nextText.StartsWith(' ')) nextText = nextText.Substring(1);
            var option = " @" + optionValue + " ";

            Value = preText + option + nextText;
            await ValueChanged.InvokeAsync(Value);

            var pos = preText.Length + option.Length;
            var js = $"document.querySelector('[_bl_{_overlayTrigger.Ref.Id}]').selectionStart = {pos};";
            js += $"document.querySelector('[_bl_{_overlayTrigger.Ref.Id}]').selectionEnd = {pos}";
            await JS.InvokeVoidAsync("eval", js);

            await HideOverlay();
            await InvokeStateHasChangedAsync();
        }
    }
}
