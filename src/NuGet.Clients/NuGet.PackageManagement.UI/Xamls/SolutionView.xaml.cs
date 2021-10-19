// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.VisualStudio.PlatformUI;
using NuGet.Versioning;
using NuGet.VisualStudio;
using NuGet.VisualStudio.Telemetry;
using Resx = NuGet.PackageManagement.UI;
using Task = System.Threading.Tasks.Task;

namespace NuGet.PackageManagement.UI
{
    /// <summary>
    /// Interaction logic for SolutionView.xaml. The DataContext is PackageSolutionDetailControlModel.
    /// </summary>
    public partial class SolutionView : UserControl
    {
        public event EventHandler<EventArgs> InstallButtonClicked;

        public event EventHandler<EventArgs> UninstallButtonClicked;

        // the list of columns that are sortable.
        private List<GridViewColumnHeader> _sortableColumns;
        private GridViewColumnHeader _requestedVersionColumn;

        public SolutionView()
        {
            InitializeComponent();

            // Change ItemContainerStyle of the _versions combobox so that
            // for a null value, a separator is generated.
            var dataTrigger = new DataTrigger();
            dataTrigger.Binding = new Binding();
            dataTrigger.Value = null;
            dataTrigger.Setters.Add(new Setter(TemplateProperty, FindResource("SeparatorControlTemplate")));

            // make sure the separator can't be selected thru keyboard navigation.
            dataTrigger.Setters.Add(new Setter(IsEnabledProperty, false));

            var style = new Style(typeof(ComboBoxItem), _versions.ItemContainerStyle);
            style.Triggers.Add(dataTrigger);
            _versions.ItemContainerStyle = style;

            _projectList.SizeChanged += ListView_SizeChanged;
            ((GridView)_projectList.View).Columns.CollectionChanged += Columns_CollectionChanged;

            //Requested Version column may not be needed, but since saved Sorting Settings are being restored at initialization time,
            //we should go ahead and create the Header column for it here with its Sort property name.
            _requestedVersionColumn = new GridViewColumnHeader();
            SortableColumnHeaderAttachedProperties.SetSortPropertyName(_requestedVersionColumn, "RequestedVersion");

            _sortableColumns = new List<GridViewColumnHeader>
            {
                _projectColumnHeader,
                _installedVersionColumnHeader,
                _requestedVersionColumn
            };

            SortByColumn(_projectColumnHeader);
        }

        private void Columns_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (sender is GridViewColumnCollection columnCollection)
            {
                for (int i = 0; i < columnCollection.Count; i++)
                {
                    if (columnCollection[i].Header is GridViewColumnHeader columnHeader)
                    {
                        // When the project list column header collection changes in any way, recalculate the tabindex
                        // of each of the columns so the tab order is in the order they appear on screen.
                        columnHeader.TabIndex = i;
                    }
                }
            }
        }

        private void SolutionView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            //Since this event will fire when closing PMUI, when the model is set to Project PMUI,
            //and when other WPF controls in the tree set their DataContext, this cast is more important than it may seem.
            if (e.NewValue is PackageSolutionDetailControlModel)
            {
                var model = e.NewValue as PackageSolutionDetailControlModel;
                if (model.IsRequestedVisible)
                {
                    GridViewColumn _versionColumn = CreateRequestedVersionColumn();
                    _gridProjects.Columns.Insert(2, _versionColumn);
                }
            }
        }

        private void UninstallButton_Clicked(object sender, RoutedEventArgs e)
        {
            UninstallButtonClicked?.Invoke(this, EventArgs.Empty);
        }

        private void InstallButton_Clicked(object sender, RoutedEventArgs e)
        {
            var model = DataContext as PackageSolutionDetailControlModel;
            string ComboboxText = _versions.Text;

            if (model.SelectedVersion == null || model.SelectedVersion.Range.OriginalString != _versions.Text)
            {
                var IsValid = VersionRange.TryParse(_versions.Text, out VersionRange versionRange);
                if (IsValid)
                {
                    model.SelectedVersion = new DisplayVersion(VersionRange.Parse(_versions.Text, true), additionalInfo: null);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SelectedVersion"));
                    _versions.Text = ComboboxText;
                }
            }
            if (InstallButtonClicked != null)
            {
                bool IsValid = VersionRange.TryParse(_versions.Text, out VersionRange versionRange);
                if (IsValid)
                {
                    model.SelectedVersion = new DisplayVersion(versionRange, additionalInfo: null);
                }
            }

            InstallButtonClicked?.Invoke(this, EventArgs.Empty);
        }

        private void ColumnHeader_Clicked(object sender, RoutedEventArgs e)
        {
            var columnHeader = sender as GridViewColumnHeader;
            if (columnHeader == null)
            {
                return;
            }

            SortByColumn(columnHeader);
        }

        public void SaveSettings(UserSettings settings)
        {
            var sortDescription = _projectList.Items.SortDescriptions.FirstOrDefault();
            if (sortDescription != default(SortDescription))
            {
                settings.SortPropertyName = sortDescription.PropertyName;
                settings.SortDirection = sortDescription.Direction;
            }
        }

        public void RestoreUserSettings(UserSettings userSettings)
        {
            if (userSettings == null)
            {
                return;
            }

            // find the column to sort
            var sortColumn = _sortableColumns.FirstOrDefault(
                column =>
                {
                    return StringComparer.OrdinalIgnoreCase.Equals(
                        SortableColumnHeaderAttachedProperties.GetSortPropertyName(obj: column),
                        userSettings.SortPropertyName);
                });

            if (sortColumn == null)
            {
                return;
            }

            UpdateColumnSorting(sortColumn, new SortDescription(userSettings.SortPropertyName, userSettings.SortDirection));
        }

        private void SortByColumn(GridViewColumnHeader sortColumn)
        {
            var sortDescription = new SortDescription();
            sortDescription.PropertyName = SortableColumnHeaderAttachedProperties.GetSortPropertyName(sortColumn);
            var sortDir = SortableColumnHeaderAttachedProperties.GetSortDirectionProperty(sortColumn);

            sortDescription.Direction = sortDir == null
                ? ListSortDirection.Ascending
                : sortDir == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;

            UpdateColumnSorting(sortColumn, sortDescription);
        }

        private void UpdateColumnSorting(GridViewColumnHeader sortColumn, SortDescription sortDescription)
        {
            // Add new sort description
            _projectList.Items.SortDescriptions.Clear();
            _projectList.Items.SortDescriptions.Add(sortDescription);

            // Upate sorting info of the column to sort on
            SortableColumnHeaderAttachedProperties.SetSortDirectionProperty(obj: sortColumn, value: sortDescription.Direction);

            // Clear sort direction of other columns and update automation properties on all columns
            foreach (var column in _sortableColumns)
            {
                if (column == sortColumn)
                {
                    UpdateHeaderAutomationProperties(column);
                    continue;
                }

                SortableColumnHeaderAttachedProperties.RemoveSortDirectionProperty(obj: column);
                UpdateHeaderAutomationProperties(column);
            }
        }

        private void UpdateHeaderAutomationProperties(GridViewColumnHeader columnHeader)
        {
            var sortDir = SortableColumnHeaderAttachedProperties.GetSortDirectionProperty(columnHeader);
            string oldHelpText = AutomationProperties.GetHelpText(columnHeader);
            string newHelpText;
            if (sortDir == ListSortDirection.Ascending)
            {
                newHelpText = Resx.Resources.Accessibility_ColumnSortedAscendingHelpText;
            }
            else if (sortDir == ListSortDirection.Descending)
            {
                newHelpText = Resx.Resources.Accessibility_ColumnSortedDescendingHelpText;
            }
            else
            {
                newHelpText = Resx.Resources.Accessibility_ColumnNotSortedHelpText;
            }

            AutomationProperties.SetHelpText(columnHeader, newHelpText);
            var peer = UIElementAutomationPeer.FromElement(columnHeader);
            peer?.RaisePropertyChangedEvent(AutomationElementIdentifiers.HelpTextProperty, oldHelpText, newHelpText);
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            NuGetUIThreadHelper.JoinableTaskFactory.RunAsync(() => CheckBoxSelectProjectsAsync(select: true))
                .PostOnFailure(nameof(SolutionView), nameof(CheckBox_Checked));
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            NuGetUIThreadHelper.JoinableTaskFactory.RunAsync(() => CheckBoxSelectProjectsAsync(select: false))
                  .PostOnFailure(nameof(SolutionView), nameof(CheckBox_Unchecked));
        }

        private async Task CheckBoxSelectProjectsAsync(bool select)
        {
            var model = DataContext as PackageSolutionDetailControlModel;
            await model.SelectAllProjectsAsync(select, CancellationToken.None);
        }

        private void ListView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // adjust the width of the "project" column so that it takes
            // up all remaining width.
            var gridView = (GridView)_projectList.View;
            var width = _projectList.ActualWidth - 3 * SystemParameters.VerticalScrollBarWidth;
            foreach (var column in gridView.Columns)
            {
                var header = (GridViewColumnHeader)column.Header;
                width -= header.ActualWidth;
            }

            var newWidth = _projectColumnHeader.ActualWidth + width;
            _projectColumn.Width = newWidth;

            // this width adjustment is only done once.
            _projectList.SizeChanged -= ListView_SizeChanged;
        }

        private void SortableColumnHeader_SizeChanged(object sender, SizeChangedEventArgs sizeChangedEventArgs)
        {
            // GridViewColumnHeader doesn't handle setting minwidth very well so we prevent it here.
            byte columnMinWidth = 60;
            string columnName = (sizeChangedEventArgs.Source as GridViewColumnHeader)?.Name;

            //"Installed" is a bit wider and can clip when the sorting indicator is applied.
            if (columnName == "_installedVersionColumnHeader")
            {
                columnMinWidth = 64;
            }
            if (sizeChangedEventArgs.NewSize.Width <= columnMinWidth)
            {
                sizeChangedEventArgs.Handled = true;
                ((GridViewColumnHeader)sender).Column.Width = columnMinWidth;
            }
        }

        private void ProjectList_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            // toggle the selection state when user presses the space bar when focus is on the ListViewItem
            var packageInstallationInfo = _projectList.SelectedItem as PackageInstallationInfo;
            if (packageInstallationInfo != null
                && e.Key == Key.Space
                && ((ListViewItem)(_projectList.ItemContainerGenerator.ContainerFromItem(_projectList.SelectedItem))).IsFocused)
            {
                packageInstallationInfo.IsSelected = !packageInstallationInfo.IsSelected;
                e.Handled = true;
            }
        }

        private void SortableColumnHeader_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            var sortableColumnHeader = sender as GridViewColumnHeader;
            if (sortableColumnHeader != null && (e.Key == Key.Space || e.Key == Key.Enter))
            {
                SortByColumn(sortableColumnHeader);
                e.Handled = true;
            }
        }

        private GridViewColumn CreateRequestedVersionColumn()
        {
            //The header for this column is always created so that saved sorting settings can be restored at initialization time.
            //Now we really need this column, so add necessary properties.
            _requestedVersionColumn.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, Resx.Resources.ColumnHeader_Requested);
            _requestedVersionColumn.Name = "_versionColumnHeader";
            _requestedVersionColumn.Click += ColumnHeader_Clicked;
            _requestedVersionColumn.Content = Resx.Resources.ColumnHeader_Requested;
            _requestedVersionColumn.HorizontalContentAlignment = HorizontalAlignment.Left;
            _requestedVersionColumn.SizeChanged += SortableColumnHeader_SizeChanged;
            _requestedVersionColumn.PreviewKeyUp += SortableColumnHeader_PreviewKeyUp;
            _requestedVersionColumn.Focusable = true;
            _requestedVersionColumn.IsTabStop = true;
            _requestedVersionColumn.SetResourceReference(FocusVisualStyleProperty, "ControlsFocusVisualStyle");

            var versionColumn = new GridViewColumn()
            {
                DisplayMemberBinding = new Binding("RequestedVersion"),
                Header = _requestedVersionColumn
            };

            return versionColumn;
        }

        private void ItemCheckBox_Toggled(object sender, RoutedEventArgs e)
        {
            var itemCheckBox = sender as CheckBox;
            var itemContainer = itemCheckBox?.FindAncestor<ListViewItem>();
            if (itemContainer is null)
            {
                return;
            }

            var newValue = (e.RoutedEvent == CheckBox.CheckedEvent);
            var oldValue = !newValue; // Assume the state has actually toggled.
            AutomationPeer peer = UIElementAutomationPeer.FromElement(itemContainer);
            peer?.RaisePropertyChangedEvent(
                TogglePatternIdentifiers.ToggleStateProperty,
                oldValue ? ToggleState.On : ToggleState.Off,
                newValue ? ToggleState.On : ToggleState.Off);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _previousFilter;
        private string PreviousFilter
        {
            get
            {
                if (_previousFilter != null) return _previousFilter;
                return _versions.SelectedValue.ToString();
            }
            set
            {
                _previousFilter = value;
                OnPropertyChanged(nameof(PreviousFilter));
            }
        }

        private bool VersionsFilter(object o)
        {
            string ComboboxText = _versions.Text;
            if (string.IsNullOrEmpty(_versions.Text)) return true;
            if (ComboboxText.Length == 0 && ComboboxText.Equals("*", StringComparison.OrdinalIgnoreCase)) return true;
            if ((ComboboxText.StartsWith("(", StringComparison.OrdinalIgnoreCase) || ComboboxText.StartsWith("[", StringComparison.OrdinalIgnoreCase)) &&
               VersionRange.TryParse(ComboboxText, out VersionRange userRange))
            {
                if (o != null && NuGetVersion.TryParse(o.ToString(), out NuGetVersion userVersion))
                {
                    if (userRange.Satisfies(userVersion))
                    {
                        return true;
                    }
                    else { return false; }
                }
                else
                {
                    return false;
                }
            }
            else
            {
                if (o != null && (o.ToString()).StartsWith(Regex.Replace(_versions.Text, @"[\*]", ""), StringComparison.OrdinalIgnoreCase)) return true;
                else return false;
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Tab:
                case Key.Enter:
                    _versions.IsDropDownOpen = false;
                    break;
                case Key.Escape:
                    _versions.IsDropDownOpen = false;
                    _versions.SelectedIndex = -1;
                    break;
                case Key.Up:
                case Key.Down:
                    if (e.Key == Key.Down) _versions.IsDropDownOpen = true;
                    break;
                case Key.Back:
                    break;
                default:
                    base.OnPreviewKeyDown(e);
                    break;
            }
        }


        protected override void OnKeyUp(KeyEventArgs e)
        {
            _versions.IsDropDownOpen = true;
            TextBox EditableTextBox = _versions.Template.FindName("PART_EditableTextBox", _versions) as TextBox;
            string ComboboxText = _versions.Text;

            DetailControlModel model = (DetailControlModel)DataContext;
            VersionRange userRequestedVersionRange = null;

            IEnumerable<NuGetVersion> versions = model.Versions.Where(v => v != null).Select(v => v.Version);
            bool userTypedAVersionRange = ComboboxText.StartsWith("(", StringComparison.OrdinalIgnoreCase) || ComboboxText.StartsWith("[", StringComparison.OrdinalIgnoreCase);

            switch (e.Key)
            {
                case Key.Enter:
                    bool isValidNuGetVersionRange = _versions.SelectedIndex >= 0 ? VersionRange.TryParse(_versions.Items[_versions.SelectedIndex].ToString(), out VersionRange requestedVersionRange) : false;
                    bool isUserInputValidNuGetVersionRange = _versions.SelectedIndex >= 0 ? VersionRange.TryParse(ComboboxText, out userRequestedVersionRange) : false;

                    if (_versions.SelectedIndex >= 0 && isValidNuGetVersionRange)
                    {
                        if (userTypedAVersionRange || userRequestedVersionRange.IsFloating)
                        {
                            NuGetVersion rangeBestVersion = userRequestedVersionRange.FindBestMatch(versions);
                            bool isBestOption = rangeBestVersion.ToString() == _versions.Items[_versions.SelectedIndex].ToString();
                            if (isBestOption)
                            {
                                _versions.SelectedValue = new DisplayVersion(userRequestedVersionRange, rangeBestVersion, additionalInfo: null);
                                _versions.Text = ComboboxText;
                                EditableTextBox.SelectionStart = 0;
                                EditableTextBox.SelectionLength = int.MaxValue;
                                _versions.IsDropDownOpen = false;

                                e.Handled = true;
                                break;
                            }
                        }

                        _versions.SelectedValue = _versions.Items[_versions.SelectedIndex];
                        _versions.Text = _versions.SelectedValue.ToString();
                        EditableTextBox.SelectionStart = 0;
                        EditableTextBox.SelectionLength = int.MaxValue;
                    }

                    _versions.IsDropDownOpen = false;
                    e.Handled = true;
                    break;
                case Key.Down:
                    if (_versions.SelectedIndex < _versions.Items.Count - 1)
                    {
                        // The separator is a null
                        if (_versions.SelectedItem == null)
                        {
                            _versions.SelectedIndex++;
                        }
                        e.Handled = true;
                    }
                    break;
                case Key.Up:
                    if (_versions.SelectedIndex >= 0)
                    {
                        // The separator is a null
                        if (_versions.SelectedItem == null)
                        {
                            _versions.SelectedIndex++;
                        }
                        e.Handled = true;
                    }
                    break;
                case Key.Left:
                case Key.Right:
                    break;
                default:
                    if (PreviousFilter != ComboboxText)
                    {
                        PreviousFilter = ComboboxText;
                        var SelectionPosition = EditableTextBox.SelectionStart;

                        if (_versions.SelectedIndex != -1 && _versions.Text != _versions.Items[_versions.SelectedIndex].ToString())
                        {
                            _versions.SelectedIndex = -1; // Clear selection. This line will also clear Text property
                            _versions.Text = ComboboxText;
                            EditableTextBox.SelectionStart = SelectionPosition;
                        }

                        // This filtering needs to be in the initilization
                        CollectionView itemsViewOriginal = CollectionViewSource.GetDefaultView(_versions.ItemsSource) as CollectionView;
                        itemsViewOriginal.Filter = VersionsFilter;

                        if (ComboboxText == "")
                        {
                            itemsViewOriginal.Refresh();
                            break;
                        }

                        bool userTypedAValidVersionRange = VersionRange.TryParse(ComboboxText, out VersionRange userRange);

                        NuGetVersion matchVersion = null;
                        if (userTypedAValidVersionRange && versions != null)
                        {
                            matchVersion = userRange.FindBestMatch(versions);
                        }

                        // Automatically select the item when the input text matches it
                        for (int i = 0; i < _versions.Items.Count; i++)
                        {
                            if (_versions.Items[i] != null && (_versions.Text == _versions.Items[i].ToString() || _versions.Items[i].ToString() == matchVersion?.ToString()))
                            {
                                _versions.SelectedIndex = i;
                                _versions.SelectedValue = _versions.Items[i];
                            }
                        }

                        _versions.Text = ComboboxText;
                        EditableTextBox.SelectionStart = SelectionPosition;

                        break;
                    }

                    e.Handled = true;
                    break;
            }
        }

        private void OpenComboBox(object sender, RoutedEventArgs e)
        {
            CollectionView itemsViewOriginal = CollectionViewSource.GetDefaultView(_versions.ItemsSource) as CollectionView;
            itemsViewOriginal.Refresh();

            _versions.IsDropDownOpen = true;
        }
    }
}
