// ItemView.cs
// 
// Copyright (C) 2008, 2010 Patrick Ulbrich
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

using System;
using Gtk;
using VolumeDB;
using Basenji.Gui.Base;
using Basenji.Icons;

namespace Basenji.Gui.Widgets
{
	public class ItemView : ViewBase
	{
		private readonly		string		STR_LOADING		= S._("Loading...");
		private readonly		string		STR_EMPTY		= S._("(empty)");
		
		private const			IconSize	ICON_SIZE		= IconSize.Button;
		
		private ItemIcons itemIcons;
		private Gdk.Pixbuf loadingIcon;
		
		private VolumeType currentVolumeType;
		private int item_col;
		
		public ItemView() {
			itemIcons = new ItemIcons(this);
			loadingIcon = this.RenderIcon(Icons.Icon.Stock_Find, ICON_SIZE);
			
			HeadersClickable = true;
			
			item_col = -1;
			
			// event handlers
			RowExpanded			+= OnRowExpanded;
			ButtonPressEvent	+= OnButtonPressEvent;
		}
		
		public void FillRoot(Volume volume) {
			if (volume == null)
				throw new ArgumentNullException("volume");
			
			TreeModel model;
			VolumeType volType = volume.GetVolumeType();
			ResetView();
			
			switch (volType) {
				case VolumeType.FileSystemVolume:
					InitView(volType, out model);
					
					// load volume root
					FileSystemVolume fsv = (FileSystemVolume)volume;
					DirectoryVolumeItem item = fsv.GetRoot();
					
					AppendDirRows((TreeStore)model, TreeIter.Zero, item);

					Model = model;
					/*ColumnsAutosize();*/
					break;
				
				case VolumeType.AudioCdVolume:
					InitView(volType, out model);
				
					// load volume root
					AudioCdVolume avol = (AudioCdVolume)volume;
					AudioCdRootVolumeItem root = avol.GetRoot();
					
					AudioTrackVolumeItem[] tracks = root.GetTracks();
					
					ListStore store = (ListStore)model;
					if (tracks.Length == 0) {
						store.AppendValues(null, STR_EMPTY, STR_EMPTY, STR_EMPTY);
					} else {
						foreach (AudioTrackVolumeItem track in tracks) {
							store.AppendValues(GetIcon(track),
						                   track.Name,
						                   (track.Artist.Length == 0 ? S._("Unknown") : track.Artist),
						                   string.Format("{0:D2}:{1:D2}", track.Duration.Minutes, track.Duration.Seconds));
						}
					}
					
					Model = model;
					/*ColumnsAutosize();*/
					break;
				default:
					throw new NotImplementedException("Items view has not been implemented for this volumetype");
			}
		}
		
		public void Clear() {
			if (Model != null) {
				if (Model is TreeStore)
					((TreeStore)Model).Clear();
				else
					((ListStore)Model).Clear();
			}
		}
		
		public VolumeType CurrentVolumeType {
			get { return currentVolumeType; }		 
		}
		
		public VolumeItem GetItem(TreeIter iter) {
			if (item_col < 0)
				return null;
			
			VolumeItem item = (VolumeItem)Model.GetValue(iter, item_col);
			return item;
		}
		
		private void InitView(VolumeType volType, out TreeModel model) {
			currentVolumeType = volType;
			TreeViewColumn col;
			
			switch (volType) {
				case VolumeType.FileSystemVolume:
					HeadersVisible = false;
				
					CellRendererPixbuf pix = new CellRendererPixbuf();
					CellRendererText txt = new CellRendererText();
					
					col = new TreeViewColumn();
					col.PackStart(pix, false);
					col.PackStart(txt, false);
					col.SetAttributes(pix, "pixbuf", 0);
					col.SetAttributes(txt, "text", 1);
					AppendColumn(col);
				
					// set up store
					model = new TreeStore(typeof(Gdk.Pixbuf),
				                      typeof(string),
				                      /* VolumeItem - not visible */
				                      typeof(FileSystemVolumeItem));
				
					item_col = 2;
					break;
				case VolumeType.AudioCdVolume:
					HeadersVisible = true;
				
					col = new TreeViewColumn(string.Empty, new CellRendererPixbuf(), "pixbuf", 0);
					col.Resizable = false;
					col.Expand = false;
					AppendColumn(col);
				
					col = new TreeViewColumn(S._("Name"), new CellRendererText(), "text", 1);
					col.Resizable = true;
					col.Expand = true;
					AppendColumn(col);
				
					col = new TreeViewColumn(S._("Artist"), new CellRendererText(), "text", 2);
					col.Resizable = true;
					col.Expand = true;
					AppendColumn(col);
				
					col = new TreeViewColumn(S._("Duration"), new CellRendererText(), "text", 3);
					col.Resizable = true;
					col.Expand = false;
					AppendColumn(col);
				
					// set up store
					model = new ListStore(typeof(Gdk.Pixbuf),
				                      typeof(string),
				                      typeof(string),
				                      typeof(string),
				                      /* VolumeItem - not visible */
				                      typeof(AudioTrackVolumeItem));
				
					item_col = 4;
					break;
				default:
					throw new NotImplementedException("View initialization has not been implemented for this volumetype");
			}
		}
		
		private void AppendDirRows(TreeStore store, TreeIter parent, DirectoryVolumeItem item) {
			bool					parentIsRoot	= parent.Equals(TreeIter.Zero);
			DirectoryVolumeItem[]	dirs			= item.GetDirectories();
			FileVolumeItem[]		files			= item.GetFiles();
			
			// if no files or dirs have been found, add an empty node
			if (dirs.Length == 0 && files.Length == 0) {
				AppendDirValues(store, parent, parentIsRoot, null, STR_EMPTY, null);
			} else {
				foreach (DirectoryVolumeItem dir in dirs) {
					TreeIter iter = AppendDirValues(store, parent, parentIsRoot, GetIcon(dir), dir.Name, dir);
					AppendDirValues(store, iter, false, loadingIcon, STR_LOADING, null);
				}
				
				foreach (FileVolumeItem file in files) {
					AppendDirValues(store, parent, parentIsRoot, GetIcon(file), file.Name, file);
				}
			}			 
		}
		
		private static TreeIter AppendDirValues(TreeStore store, TreeIter parent, bool parentIsRoot, Gdk.Pixbuf icon, string name, VolumeItem item) {
			if (parentIsRoot)
				return store.AppendValues(icon, name, item);
			else
				return store.AppendValues(parent, icon, name, item);
		}
		
		private Gdk.Pixbuf GetIcon(VolumeItem item) {
			return itemIcons.GetIconForItem(item, ICON_SIZE);	
		}
		
		private void OnRowExpanded(object o, RowExpandedArgs args) {
			switch (CurrentVolumeType) {			   
				case VolumeType.FileSystemVolume:
					TreeStore store = (TreeStore)Model;
					
					// get child node of expanded node
					TreeIter child;
					store.IterChildren(out child, args.Iter);

					// test if the first child is the "loading" child node
					if ((GetItem(child) == null) && ((string)Model.GetValue(child, 1) == STR_LOADING)) {
						// append dir children
						DirectoryVolumeItem dir = (DirectoryVolumeItem)GetItem(args.Iter);
						AppendDirRows(store, args.Iter, dir);
					
						// remove "loading" child node
						store.Remove(ref child);
					}
					break;
				default:
					throw new NotImplementedException("View for this VolumeType has not been implemented yet");
			}
									
		}
		
		[GLib.ConnectBefore()]
		private void OnButtonPressEvent(object o, ButtonPressEventArgs args) {
			if (args.Event.Type == Gdk.EventType.TwoButtonPress) {
				TreeIter iter;				  
				if (!GetSelectedIter(out iter))
					return;

				if (Model.IterHasChild(iter)) {
					TreePath path = Model.GetPath(iter);
					if (GetRowExpanded(path))
						CollapseRow(path);
					else
						ExpandRow(path, false);
				}
			}
		}		 
	}
}