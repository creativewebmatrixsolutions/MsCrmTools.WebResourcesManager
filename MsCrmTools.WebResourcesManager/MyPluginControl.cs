﻿using McTools.Xrm.Connection;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using MscrmTools.WebresourcesManager.AppCode;
using MscrmTools.WebresourcesManager.AppCode.Args;
using MscrmTools.WebresourcesManager.AppCode.Exceptions;
using MscrmTools.WebresourcesManager.CustomControls;
using MscrmTools.WebresourcesManager.Forms;
using MscrmTools.WebresourcesManager.Forms.Contents;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace MscrmTools.WebresourcesManager
{
    public partial class MyPluginControl : PluginControlBase, IShortcutReceiver, IGitHubPlugin
    {
        private FolderNode contextFolderNode;
        private Webresource contextStripResource;
        private InvalidFilenamesDialog ifnd;
        private LoadResourcesSettings lastSettings;
        private PendingUpdatesDialog pud;
        private ResourcePropertiesDialog rpd;
        private SettingsDialog sd;
        private WebresourcesTreeView tv;

        #region IGitHubPlugin

        public virtual string RepositoryName => "MsCrmTools.WebResourcesManager";
        public virtual string UserName => "MscrmTools";

        #endregion IGitHubPlugin

        public MyPluginControl()
        {
            InitializeComponent();

            SetTheme();

            tv = new WebresourcesTreeView(this);
            tv.ResourceDisplayRequested += Tv_ResourceDisplayRequested;
            tv.ContextMenuRequested += Tv_ContextMenuRequested;
            tv.ResourceSelected += Tv_ResourceSelected;
            tv.ShowInvalidFilesRequested += Tv_ShowInvalidFilesRequested;
            tv.ShowPendingUpdatesRequested += Tv_ShowPendingUpdatesRequested;
            tv.Show(dpMain, Settings.Instance.TreeviewDockState);

            pud = new PendingUpdatesDialog(this);
            pud.Show(dpMain, Settings.Instance.PendingUpdatesDockState);

            rpd = new ResourcePropertiesDialog();
            rpd.Show(dpMain, Settings.Instance.PropertiesDockState);

            sd = new SettingsDialog();
            sd.Show(dpMain, Settings.Instance.SettingsDockState);

            tv.Show(dpMain, Settings.Instance.TreeviewDockState);
        }

        public ObservableCollection<Webresource> WebresourcesCache { get; set; } = new ObservableCollection<Webresource>();

        public override void ClosingPlugin(PluginCloseInfo info)
        {
            Settings.Instance.TreeviewDockState = tv.DockState;
            Settings.Instance.PendingUpdatesDockState = pud.DockState;
            Settings.Instance.SettingsDockState = sd.DockState;
            Settings.Instance.PropertiesDockState = rpd.DockState;

            Settings.Instance.Save();

            base.ClosingPlugin(info);
        }

        public void Export(bool toCsv)
        {
            var separator = toCsv ? "," : "\t";
            var selectedWebresources = WebresourcesCache.Where(r => r.Node.Checked).ToList();
            if (selectedWebresources.Any())
            {
                var sb = new StringBuilder();
                foreach (var resource in selectedWebresources)
                {
                    sb.AppendLine(
                        $"{resource.Name}{separator}{resource.FormattedType}{separator}{resource.DisplayName}{separator}{resource.Description}");
                }

                try
                {
                    if (toCsv)
                    {
                        var sfd = new SaveFileDialog { Filter = @"CSV file (*.csv)|*.csv" };
                        if (sfd.ShowDialog(this) == DialogResult.OK)
                        {
                            using (var fileStream = new FileStream(sfd.FileName, FileMode.OpenOrCreate))
                            using (var writer = new StreamWriter(fileStream, Encoding.Default))
                            {
                                writer.WriteLine(sb.ToString());
                            }
                        }
                    }
                    else
                    {
                        Clipboard.SetText(sb.ToString());
                    }
                }
                catch (Exception e)
                {
                    MessageBox.Show(this, $@"An error occured while exporting webresources: {e.Message}", @"Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public void ShowSettings()
        {
            if (sd.IsDisposed)
            {
                sd = new SettingsDialog();
            }

            sd.ShowDocked();
        }

        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            tv.Service = newService;
            tv.OrganizationMajorVersion = detail.OrganizationMajorVersion;

            base.UpdateConnection(newService, detail, actionName, parameter);
        }

        //public void ShowContentNotUpdated()
        //{
        //    Invoke(new Action(() =>
        //    {
        //        var message = @"Whereas update action succeeded, it seems that the content of the webresource did not change on the database. This might be due to a temporary problem on the target organization";
        //        MessageBox.Show(this, message, @"Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        //    }));
        //}
        internal void DisplayWaitingForUpdatePanel()
        {
            tv.DisplayWaitingForUpdatePanel();
        }

        internal int GetOrgMajorVersion()
        {
            return ConnectionDetail?.OrganizationMajorVersion ?? 0;
        }

        private void CloseOpenedContents()
        {
            var toClose = dpMain.Contents.OfType<BaseContentForm>().ToList();
            for (var i = 0; i < toClose.Count; i++)
            {
                toClose[i].Close();
            }
        }

        private void cmsTabs_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem == cmsTabsCloseThis)
            {
                (dpMain.ActiveContent as BaseContentForm)?.Close();
            }
            else if (e.ClickedItem == cmsTabsCloseExceptThis)
            {
                var currentContent = dpMain.ActiveContent as BaseContentForm;
                var list = dpMain.Contents.OfType<BaseContentForm>().Where(bcf => bcf != currentContent).ToList();
                foreach (var content in list)
                {
                    if (content != currentContent)
                    {
                        content.Close();
                    }
                }
            }
            else if (e.ClickedItem == cmsTabsCloseAll)
            {
                var list = dpMain.Contents.OfType<BaseContentForm>().ToList();
                foreach (var content in list)
                {
                    content.Close();
                }
            }
        }

        private void cmsWebresourceTreeview_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem != tsmiAddNewResource)
                cmsWebresourceTreeview.Hide();

            if (e.ClickedItem == tsmiProperties)
            {
                if (rpd.IsDisposed)
                {
                    rpd = new ResourcePropertiesDialog();
                }

                rpd.Resource = contextStripResource;
                rpd.ShowDocked();
            }
            else if (e.ClickedItem == tsmiSetDependencies)
            {
                var dialog = new DependencyDialog(contextStripResource, this);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    contextStripResource.DependencyXml = dialog.UpdatedDependencyXml;
                }
            }
            else if (e.ClickedItem == tsmiCopyNameToClipboard)
            {
                Clipboard.SetData(DataFormats.StringFormat, contextStripResource.Name);
            }
            else if (e.ClickedItem == tsmiOpenInCrm)
            {
                if (contextStripResource.Id == Guid.Empty)
                {
                    MessageBox.Show(this, @"This web resource does not exist on the CRM organization or is not synced", @"Warning",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Process.Start($"{ConnectionDetail.WebApplicationUrl}/main.aspx?id={contextStripResource.Id}&etc=9333&pagetype=webresourceedit");
            }
            else if (e.ClickedItem == tsmiDelete)
            {
                ExecuteMethod(DeleteWebresource, contextStripResource);
            }
            else if (e.ClickedItem == tsmiUpdate)
            {
                var us = new UpdateResourcesSettings
                {
                    Webresources = new List<Webresource> { contextStripResource },
                };

                ExecuteMethod(UpdateWebResources, us);
            }
            else if (e.ClickedItem == tsmiUpdatePublish)
            {
                var us = new UpdateResourcesSettings
                {
                    Webresources = new List<Webresource> { contextStripResource },
                    Publish = true
                };

                ExecuteMethod(UpdateWebResources, us);
            }
            else if (e.ClickedItem == tsmiUpdatePublishAdd)
            {
                var us = new UpdateResourcesSettings
                {
                    Webresources = new List<Webresource> { contextStripResource },
                    Publish = true,
                    AddToSolution = true
                };

                ExecuteMethod(UpdateWebResources, us);
            }
            else if (e.ClickedItem == tsmiGetLatest)
            {
                try
                {
                    contextStripResource.GetLatestVersion();
                }
                catch (Exception error)
                {
                    MessageBox.Show(this, error.Message, @"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else if (e.ClickedItem == tsmiRefreshFileFromDisk)
            {
                if (string.IsNullOrEmpty(contextStripResource.FilePath))
                {
                    MessageBox.Show(this, @"This webresource is not synced with a local file", @"Warning",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                contextStripResource.RefreshFromDisk();
            }
            else if (e.ClickedItem == tsmiUpdateFolderFromDisk)
            {
                if (string.IsNullOrEmpty(contextFolderNode.FolderPath))
                {
                    MessageBox.Show(this,
                        @"This folder node is not synced with a local folder. Cannot refresh from disk", @"Warning",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var invalidFileNames = new List<string>();
                tv.RefreshFolderNodeContent(contextFolderNode, invalidFileNames, null);

                if (invalidFileNames.Any())
                {
                    if (ifnd == null || ifnd.IsDisposed)
                    {
                        ifnd = new InvalidFilenamesDialog();
                    }

                    ifnd.InvalidFiles = invalidFileNames;
                    ifnd.ShowDocked(DockState.DockBottom);
                }
            }
            else if (e.ClickedItem == tsmiRenameWebresource)
            {
                var renameDialog = new RenameWebResourceDialog(contextStripResource.Name, ConnectionDetail?.OrganizationMajorVersion ?? -1);

                if (renameDialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (contextStripResource.Name != renameDialog.WebResourceName)
                    {
                        ExecuteMethod(RenameWebresource, renameDialog.WebResourceName);
                    }
                }
            }
            else if (e.ClickedItem == tsmiAddNewFolder)
            {
                var newFolderDialog = new NewFolderDialog(ConnectionDetail?.OrganizationMajorVersion ?? -1);
                if (newFolderDialog.ShowDialog(this) == DialogResult.OK)
                {
                    tv.AddSingleFolder(contextFolderNode, newFolderDialog.FolderName);
                }
            }
            else if (e.ClickedItem == tsmiAddExistingFile)
            {
                var ofd = new OpenFileDialog { Multiselect = true, Title = @"Select file(s) to add as webresource(s)" };

                if (ofd.ShowDialog(ParentForm) == DialogResult.OK)
                {
                    var invalidFileNames = new List<string>();
                    tv.AddFilesAsNodes(contextFolderNode, ofd.FileNames.ToList(), invalidFileNames);

                    if (invalidFileNames.Any())
                    {
                        if (ifnd == null || ifnd.IsDisposed)
                        {
                            ifnd = new InvalidFilenamesDialog();
                        }

                        ifnd.InvalidFiles = invalidFileNames;
                        ifnd.ShowDocked(DockState.DockBottom);
                    }
                }
            }
            else if (e.ClickedItem == tsmiNewCss)
            {
                tv.AddNewWebresource(contextFolderNode, WebresourceType.Css);
            }
            else if (e.ClickedItem == tsmiNewData)
            {
                tv.AddNewWebresource(contextFolderNode, WebresourceType.Data);
            }
            else if (e.ClickedItem == tsmiNewHtml)
            {
                tv.AddNewWebresource(contextFolderNode, WebresourceType.WebPage);
            }
            else if (e.ClickedItem == tsmiNewResx)
            {
                tv.AddNewWebresource(contextFolderNode, WebresourceType.Resx);
            }
            else if (e.ClickedItem == tsmiNewScript)
            {
                tv.AddNewWebresource(contextFolderNode, WebresourceType.Script);
            }
            else if (e.ClickedItem == tsmiNewXsl)
            {
                tv.AddNewWebresource(contextFolderNode, WebresourceType.Xsl);
            }
            else if (e.ClickedItem == tsmiExpand)
            {
                contextFolderNode.ExpandAll();
            }
            else if (e.ClickedItem == tsmiCollapse)
            {
                contextFolderNode.Collapse(false);
            }
        }

        private void DeleteWebresource(Webresource resource)
        {
            if (DialogResult.Yes == MessageBox.Show(this,
                    @"This webresource will be deleted from the organization if it exists.

Are you sure you want to delete this webresource?",
                    @"Question",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question))
            {
                WorkAsync(new WorkAsyncInfo
                {
                    Message = @"Deleting webresource...",
                    AsyncArgument = resource,
                    Work = (bw, e) =>
                    {
                        ((Webresource)e.Argument).Delete(Service);
                        WebresourcesCache.Remove((Webresource)e.Argument);
                    },
                    PostWorkCallBack = e =>
                    {
                        if (e.Error != null)
                        {
                            MessageBox.Show(this, $@"An error occured while deleting the webresource: {e.Error.Message}",
                                @"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }

                        resource.Node.Remove();
                    }
                });
            }
        }

        private void DisplayContent(Webresource resource)
        {
            if (resource == null) return;

            var existingContent = dpMain.Contents.OfType<BaseContentForm>().FirstOrDefault(c => c.Resource == resource);
            if (existingContent != null)
            {
                existingContent.Show(dpMain, existingContent.DockState);
                return;
            }

            BaseContentForm content = null;

            switch (resource.Type)
            {
                case (int)WebresourceType.Gif:
                case (int)WebresourceType.Jpg:
                case (int)WebresourceType.Png:
                case (int)WebresourceType.Vector:
                    {
                        content = new ImageContentForm(this, resource);
                    }
                    break;

                case (int)WebresourceType.Resx:
                    {
                        content = new ResxContentForm(this, resource);
                    }
                    break;

                case (int)WebresourceType.Data:
                case (int)WebresourceType.Xsl:
                case (int)WebresourceType.Css:
                case (int)WebresourceType.Script:
                case (int)WebresourceType.WebPage:
                    {
                        content = new CodeEditorForm(this, resource);
                    }
                    break;

                case (int)WebresourceType.Ico:
                case (int)WebresourceType.Silverlight:
                    {
                        MessageBox.Show(this, @"No visualization available for this webresource type", @"Warning",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    break;
            }

            if (content != null)
            {
                content.TabPageContextMenuStrip = cmsTabs;
                content.Show(dpMain, DockState.Document);
            }
        }

        private void DisplayContextMenuStripItems(bool visible)
        {
            foreach (ToolStripItem item in cmsWebresourceTreeview.Items)
            {
                item.Visible = visible;
            }

            tsmiAddNewFolder.Visible = !visible;
            tsmiAddExistingFile.Visible = !visible;
            tsmiAddNewResource.Visible = !visible;
            tsmiCollapse.Visible = !visible;
            tsmiExpand.Visible = !visible;
            tsmiUpdateFolderFromDisk.Visible = !visible;

            tssFolder1.Visible = !visible;
            tssFolder2.Visible = !visible;

            tsmiSetDependencies.Visible = visible && ConnectionDetail?.OrganizationMajorVersion >= 9;
            tssResource5.Visible = visible && ConnectionDetail?.OrganizationMajorVersion >= 9;
        }

        private void FindUnunsedResources()
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Starting analysis...",
                Work = (bw, e) =>
                {
                    var unusedWebResources = new List<Webresource>();
                    int i = 1;
                    foreach (var resource in WebresourcesCache)
                    {
                        bw.ReportProgress(i * 100 / WebresourcesCache.Count, "Analyzing web resource " + resource.Name + "...");

                        if (!resource.HasDependencies(Service))
                        {
                            unusedWebResources.Add(resource);
                        }
                        i++;
                    }

                    e.Result = unusedWebResources;
                },
                ProgressChanged = e =>
                    {
                        SetWorkingMessage($"{e.ProgressPercentage}% - {e.UserState}");
                    },
                PostWorkCallBack = e =>
                {
                    var dialog = new UnusedWebResourcesListDialog((List<Webresource>)e.Result, Service);
                    dialog.ShowDialog(this);
                }
            });
        }

        private void MyPluginControl_Load(object sender, EventArgs e)
        {
        }

        private void RenameWebresource(string newName)
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = @"Renaming webresource...",
                AsyncArgument = newName,
                Work = (bw, e) =>
                {
                    contextStripResource.Rename(e.Argument.ToString());
                },
                PostWorkCallBack = e =>
                {
                    if (e.Error != null)
                    {
                        MessageBox.Show(this, $@"An error occured while renaming the webresource: {e.Error.Message}",
                            @"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            });
        }

        private void SetTheme()
        {
            if (XrmToolBox.Options.Instance.Theme != null)
            {
                switch (XrmToolBox.Options.Instance.Theme)
                {
                    case "Blue theme":
                        {
                            var theme = new VS2015BlueTheme();
                            dpMain.Theme = theme;
                        }
                        break;

                    case "Light theme":
                        {
                            var theme = new VS2015LightTheme();
                            dpMain.Theme = theme;
                        }
                        break;

                    case "Dark theme":
                        {
                            var theme = new VS2015DarkTheme();
                            dpMain.Theme = theme;
                        }
                        break;
                }
            }
        }

        private void tsddTools_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem == findWebresourcesWithoutDependenciesToolStripMenuItem)
            {
                if (WebresourcesCache.Count == 0)
                {
                    MessageBox.Show(this, @"Please load webresources from an organization first to use this feature",
                        @"Warning",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ExecuteMethod(FindUnunsedResources);
            }
            else if (e.ClickedItem == toCSVToolStripMenuItem)
            {
                Export(true);
            }
            else if (e.ClickedItem == toClipboardToolStripMenuItem)
            {
                Export(false);
            }
        }

        private void tsddWindow_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem == tsmiWebresourcesExplorer)
            {
                tv.ShowDocked();
            }
            else if (e.ClickedItem == tsmiPendingUpdates)
            {
                pud.ShowDocked();
            }
            else if (e.ClickedItem == tsmiMainProperties)
            {
                rpd.ShowDocked();
            }
            else if (e.ClickedItem == tsmiSettings)
            {
                sd.ShowDocked();
            }
        }

        private void Tv_ContextMenuRequested(object sender, NodeSelectedEventArgs e)
        {
            if (e.Node is FolderNode fn)
            {
                DisplayContextMenuStripItems(false);

                tsmiCollapse.Enabled = e.Node.IsExpanded;
                tsmiExpand.Enabled = !e.Node.IsExpanded;

                contextFolderNode = fn;
            }
            else if (e.Node is WebresourceNode wn)
            {
                DisplayContextMenuStripItems(true);
                contextStripResource = wn.Resource;
            }

            cmsWebresourceTreeview.Tag = e.Node;
            cmsWebresourceTreeview.Show(tv, new Point(e.Location.X, e.Location.Y + 20));
        }

        private void Tv_ResourceDisplayRequested(object sender, ResourceEventArgs e)
        {
            rpd.Resource = e.Resource;
            DisplayContent(e.Resource);
        }

        private void Tv_ResourceSelected(object sender, ResourceEventArgs e)
        {
            if (rpd == null)
            {
                rpd = new ResourcePropertiesDialog();
            }

            rpd.Resource = e.Resource;
        }

        private void Tv_ShowInvalidFilesRequested(object sender, InvalidFilesEventArgs e)
        {
            if (ifnd == null || ifnd.IsDisposed)
            {
                ifnd = new InvalidFilenamesDialog { DockPanel = dpMain };
            }

            ifnd.InvalidFiles = e.InvalidFilesList;
            ifnd.ShowDocked(DockState.DockBottom);
        }

        private void Tv_ShowPendingUpdatesRequested(object sender, EventArgs e)
        {
            pud.ShowDocked();
        }

        #region Menu Items Events

        private void tsddCrmMenu_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem == TsmiLoadWebResources)
            {
                ExecuteMethod(LoadWebresourcesGeneral, false);
            }
            else if (e.ClickedItem == TsmiLoadWebResourcesFromASpecificSolution)
            {
                ExecuteMethod(LoadWebresourcesGeneral, true);
            }
            else if (e.ClickedItem == tsmiReloadFromCurrentSolution)
            {
                LoadWebresources(lastSettings);
            }
            else if (e.ClickedItem == tsmiUpdateWebresources)
            {
                var us = new UpdateResourcesSettings
                {
                    Webresources = WebresourcesCache.Where(r => r.Node.Checked)
                };

                ExecuteMethod(UpdateWebResources, us);
            }
            else if (e.ClickedItem == tsmiUpdatePublishWebresources)
            {
                var us = new UpdateResourcesSettings
                {
                    Webresources = WebresourcesCache.Where(r => r.Node != null && r.Node.Checked),
                    Publish = true
                };

                ExecuteMethod(UpdateWebResources, us);
            }
            else if (e.ClickedItem == tsmiUpdatePublishAddWebresources)
            {
                var us = new UpdateResourcesSettings
                {
                    Webresources = WebresourcesCache.Where(r => r.Node.Checked),
                    Publish = true,
                    AddToSolution = true
                };

                ExecuteMethod(UpdateWebResources, us);
            }
        }

        #endregion Menu Items Events

        #region CRM

        public void LoadWebresourcesGeneral(bool fromSolution)
        {
            lastSettings = new LoadResourcesSettings();

            // If from solution, display the solution picker so that user can
            // select the solution containing the web resources he wants to
            // display
            if (fromSolution)
            {
                var sPicker = new SolutionPicker(Service) { StartPosition = FormStartPosition.CenterParent };
                if (sPicker.ShowDialog(ParentForm) == DialogResult.OK)
                {
                    lastSettings.Solution = sPicker.SelectedSolution;
                    lastSettings.LoadAllWebresources = sPicker.LoadAllWebresources;

                    if (lastSettings.LoadAllWebresources)
                    {
                        lastSettings.FilterByLcid = sPicker.FilterByLcid;
                    }
                }
                else
                {
                    return;
                }
            }

            if (!lastSettings.LoadAllWebresources)
            {
                // Display web resource types selection dialog
                var dialog = new WebResourceTypeSelectorDialog(ConnectionDetail?.OrganizationMajorVersion ?? -1);
                if (dialog.ShowDialog(ParentForm) == DialogResult.OK)
                {
                    lastSettings.TypesToload = dialog.TypesToLoad;
                    lastSettings.FilterByLcid = dialog.FilterByLcid;
                }
                else
                {
                    return;
                }
            }

            LoadWebresources(lastSettings);
        }

        public void PerformUpdate(UpdateResourcesSettings us)
        {
            ExecuteMethod(UpdateWebResources, us);
        }

        public void UpdateWebResources(UpdateResourcesSettings us)
        {
            if (us.AddToSolution)
            {
                var sPicker = new SolutionPicker(Service, true) { StartPosition = FormStartPosition.CenterParent };

                if (sPicker.ShowDialog(this) == DialogResult.OK)
                {
                    us.SolutionUniqueName = sPicker.SelectedSolution["uniquename"].ToString();
                }
                else
                {
                    return;
                }
            }

            // TODO Disable controls during update

            tv.Service = Service;

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Updating web resources...",
                AsyncArgument = us,
                Work = (bw, e) =>
                {
                    var settings = (UpdateResourcesSettings)e.Argument;
                    var resourcesUpdated = new List<Webresource>();
                    var resources = new List<Webresource>();

                    // Add Regular Resources, and Associated Web Resources
                    foreach (var resource in settings.Webresources)
                    {
                        resources.Add(resource);
                        if (resource.AssociatedResources != null)
                        {
                            resources.AddRange(resource.AssociatedResources);
                        }
                    }

                    foreach (var resource in resources)
                    {
                        try
                        {
                            bw.ReportProgress(1, $"Updating {resource}...");
                            resource.Update(Service, us.Overwrite);

                            resourcesUpdated.Add(resource);
                            resource.LastException = null;
                        }
                        catch (Exception error)
                        {
                            resource.LastException = error;
                        }
                    }

                    // Process post Update command
                    if (!string.IsNullOrEmpty(Settings.Instance.AfterUpdateCommand))
                    {
                        foreach (var webResource in resourcesUpdated)
                        {
                            EventManager.ActAfterUpdate(webResource, Settings.Instance);
                        }
                    }

                    // if publish
                    if (settings.Publish && resourcesUpdated.Count > 0)
                    {
                        bw.ReportProgress(2, "Publishing web resources...");

                        Webresource.Publish(resourcesUpdated, Service);
                    }

                    // Process post Publish command
                    if (!string.IsNullOrEmpty(Settings.Instance.AfterPublishCommand))
                    {
                        foreach (var webResource in resourcesUpdated)
                        {
                            EventManager.ActAfterPublish(webResource, Settings.Instance);
                        }
                    }

                    if (settings.AddToSolution && !string.IsNullOrEmpty(settings.SolutionUniqueName) && resourcesUpdated.Count > 0)
                    {
                        bw.ReportProgress(3, "Adding web resources to solution...");
                        Webresource.AddToSolution(resourcesUpdated, settings.SolutionUniqueName, Service);
                    }

                    e.Result = new UpdateResourcesResult
                    {
                        FaultedResources = resources.Except(resourcesUpdated),
                        Publish = settings.Publish,
                        AddToSolution = settings.AddToSolution,
                        SolutionUniqueName = settings.SolutionUniqueName
                    };
                },
                ProgressChanged = e => { SetWorkingMessage(e.UserState.ToString()); },
                PostWorkCallBack = e =>
                {
                    if (e.Error != null)
                    {
                        MessageBox.Show(this, e.Error.Message, @"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var result = (UpdateResourcesResult)e.Result;

                    // Identifies resources with concurrency behavior error
                    //var unsyncResources = result.FaultedResources.Where(r =>
                    //    r.LastException is FaultException<OrganizationServiceFault>
                    //    && ((FaultException<OrganizationServiceFault>)r.LastException).Detail.ErrorCode ==
                    //    -2147088254).ToList();
                    var unsyncResources =
                        result.FaultedResources.Where(r => r.LastException is MoreRecentRecordExistsException).ToList();
                    var otherResources = result.FaultedResources.Except(unsyncResources).ToList();

                    if (unsyncResources.Any() || otherResources.Any())
                    {
                        var dialog = new ConcurrencySummaryDialog(unsyncResources, otherResources);
                        if (dialog.ShowDialog(this) == DialogResult.Retry)
                        {
                            var retryUs = new UpdateResourcesSettings
                            {
                                Webresources = unsyncResources,
                                Publish = result.Publish,
                                AddToSolution = result.AddToSolution,
                                SolutionUniqueName = result.SolutionUniqueName,
                                Overwrite = true
                            };

                            UpdateWebResources(retryUs);
                        }
                    }
                }
            });
        }

        private void LoadWebresources(LoadResourcesSettings settings)
        {
            tv.Enabled = false;
            tv.OrganizationMajorVersion = ConnectionDetail?.OrganizationMajorVersion ?? -1;
            tv.Service = Service;

            CloseOpenedContents();

            WorkAsync(new WorkAsyncInfo("Loading web resources...", e =>
            {
                var request = new RetrieveProvisionedLanguagesRequest();
                var response = (RetrieveProvisionedLanguagesResponse)Service.Execute(request);

                var args = (LoadResourcesSettings)e.Argument;

                var items = Webresource.RetrieveWebresources(this, Service, args.Solution?.Id ?? Guid.Empty,
                     args.TypesToload,
                     args.FilterByLcid,
                     response.RetrieveProvisionedLanguages);
                e.Result = items;

                WebresourcesCache.Clear();

                foreach (var item in items)
                {
                    WebresourcesCache.Add(item);
                }
            })
            {
                AsyncArgument = settings,
                PostWorkCallBack = e =>
                {
                    tv.Enabled = true;
                    tv.DisplayNodes((IEnumerable<Webresource>)e.Result, settings.Solution);

                    if (settings.Solution != null)
                    {
                        tsmiReloadFromCurrentSolution.Text = string.Format(
                            tsmiReloadFromCurrentSolution.Tag.ToString(),
                            settings.Solution.GetAttributeValue<string>("friendlyname"),
                            settings.Solution.GetAttributeValue<string>("version"));
                        tsmiReloadFromCurrentSolution.Visible = true;
                    }
                    else
                    {
                        tsmiReloadFromCurrentSolution.Visible = false;
                    }
                }
            });
        }

        #endregion CRM

        #region Disk

        private void LoadFromDisk()
        {
            try
            {
                // Let the user decides where to find files
                var fbd = new CustomFolderBrowserDialog(true);

                if (!string.IsNullOrWhiteSpace(Settings.Instance.LastFolderUsed) && Directory.Exists(Settings.Instance.LastFolderUsed))
                {
                    fbd.FolderPath = Settings.Instance.LastFolderUsed;
                }

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    Settings.Instance.LastFolderUsed = fbd.FolderPath;
                    Settings.Instance.Save();

                    if (fbd.FolderPath.EndsWith("\\"))
                        fbd.FolderPath = fbd.FolderPath.Substring(0, fbd.FolderPath.Length - 1);

                    CloseOpenedContents();

                    var invalidFilenames = new List<string>();
                    WebresourcesCache.Clear();
                    tv.DisplayWaitingForUpdatePanel();
                    var resources = Webresource.RetrieveFromDirectory(this, fbd.FolderPath, fbd.ExtensionsToLoad, invalidFilenames, ConnectionDetail?.OrganizationMajorVersion ?? -1);

                    tv.DisplayNodes(resources, null, Settings.Instance.ExpandAllOnLoadingResources);

                    if (invalidFilenames.Count > 0)
                    {
                        if (ifnd == null || ifnd.IsDisposed)
                        {
                            ifnd = new InvalidFilenamesDialog();
                        }

                        ifnd.InvalidFiles = invalidFilenames;
                        ifnd.Show(dpMain, DockState.DockBottom);
                    }
                    else if (ifnd != null && !ifnd.IsDisposed)
                    {
                        ifnd.InvalidFiles = invalidFilenames;
                    }
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(this, $@"An error occured while loading webresources from disk: {error.Message}", @"Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveToDisk(IEnumerable<Webresource> resources, bool withRoot = false)
        {
            var fbd = new CustomFolderBrowserDialog(true, false);
            if (!string.IsNullOrEmpty(Settings.Instance.LastFolderUsed))
            {
                fbd.FolderPath = Settings.Instance.LastFolderUsed;
            }

            if (fbd.ShowDialog() == DialogResult.OK)
            {
                Settings.Instance.LastFolderUsed = fbd.FolderPath;
                Settings.Instance.Save();

                foreach (var resource in resources)
                {
                    string[] partPath = resource.Name.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    string path = fbd.FolderPath;

                    if (withRoot)
                    {
                        for (int i = 0; i < partPath.Length - 1; i++)
                        {
                            path = Path.Combine(path, partPath[i]);

                            if (!Directory.Exists(path))
                            {
                                Directory.CreateDirectory(path);
                            }
                        }
                    }

                    if (resource.Node.Parent is FolderNode fldNode)
                    {
                        fldNode.FolderPath = path;
                        fldNode.Synced = true;
                    }

                    path = Path.Combine(path, partPath[partPath.Length - 1]);

                    if (resource.Content?.Length > 0)
                    {
                        byte[] bytes = Convert.FromBase64String(resource.Content);
                        File.WriteAllBytes(path, bytes);
                    }
                    else
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                        File.Create(path).Dispose();
                    }

                    resource.FilePath = path;
                }
            }
        }

        private void tsddFileMenu_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem == tsmiLoadFromDisk)
            {
                if (WebresourcesCache.Any(r => r.State == WebresourceState.Draft))
                {
                    var message =
                        "Some webresources are in draft status.\n\nAre you sure you want to close current webresources and load new ones?";
                    if (MessageBox.Show(this, message, @"Question", MessageBoxButtons.YesNo, MessageBoxIcon.Question) ==
                        DialogResult.No)
                    {
                        return;
                    }
                }

                LoadFromDisk();
            }
            else if (e.ClickedItem == tsmiSaveToDiskWithRoots)
            {
                SaveToDisk(WebresourcesCache.Where(r => r?.Node?.Checked == true), true);
            }
            else if (e.ClickedItem == tsmiSaveToDisk)
            {
                SaveToDisk(WebresourcesCache.Where(r => r?.Node?.Checked == true));
            }
        }

        #endregion Disk

        #region IShortcutReceiver

        private bool isCtrlM, isCtrlK;

        public void ReceiveKeyDownShortcut(KeyEventArgs e)
        {
            var activeContent = dpMain.ActiveContent;

            if (e.Control && e.KeyCode == Keys.S)
            {
                if (activeContent is BaseContentForm bcf)
                {
                    bcf.Resource.Save();
                }

                isCtrlM = false;
                isCtrlK = false;
            }
            else if (e.Control && e.KeyCode == Keys.U)
            {
                if (!isCtrlK)
                {
                    if (activeContent is BaseContentForm bcf)
                    {
                        var us = new UpdateResourcesSettings
                        {
                            Webresources = new List<Webresource> { bcf.Resource },
                            Publish = true
                        };

                        ExecuteMethod(UpdateWebResources, us);
                    }
                }
                else if (activeContent is CodeEditorForm cef)
                {
                    cef.UncommentSelectedLines();
                }

                isCtrlM = false;
                isCtrlK = false;
            }
            else if (e.Control && e.KeyCode == Keys.G)
            {
                if (activeContent is CodeEditorForm cef)
                {
                    cef.GoToLine();
                }

                isCtrlM = false;
                isCtrlK = false;
            }
            else if (e.Control && e.KeyCode == Keys.F)
            {
                if (activeContent is CodeEditorForm cef)
                {
                    cef.Find(false);
                }
                isCtrlM = false;
                isCtrlK = false;
            }
            else if (e.Control && e.KeyCode == Keys.H)
            {
                if (activeContent is CodeEditorForm cef)
                {
                    cef.Find(true);
                }
                isCtrlM = false;
                isCtrlK = false;
            }
            else if (e.Control && e.KeyCode == Keys.M)
            {
                isCtrlM = true;
                isCtrlK = false;
            }
            else if (e.Control && e.KeyCode == Keys.K)
            {
                isCtrlM = false;
                isCtrlK = true;
            }
            else if (e.Control && e.KeyCode == Keys.O)
            {
                if (isCtrlM && activeContent is CodeEditorForm cef)
                {
                    cef.ContractFolds();
                }

                isCtrlM = false;
                isCtrlK = false;
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                if (activeContent is CodeEditorForm cef)
                    if (isCtrlK)
                    {
                        cef.CommentSelectedLines();
                    }
                    else
                    {
                        cef.Copy();
                    }

                isCtrlM = false;
                isCtrlK = false;
            }
            else
            {
                isCtrlM = false;
                isCtrlK = false;
            }
        }

        public void ReceiveKeyPressShortcut(KeyPressEventArgs e)
        {
        }

        public void ReceiveKeyUpShortcut(KeyEventArgs e)
        {
        }

        public void ReceivePreviewKeyDownShortcut(PreviewKeyDownEventArgs e)
        {
        }

        #endregion IShortcutReceiver
    }
}