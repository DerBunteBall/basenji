// AbstractImport.cs
// 
// Copyright (C) 2010 Patrick Ulbrich
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
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.ComponentModel;
using Platform.Common.Diagnostics;

namespace VolumeDB.Import
{
	public abstract class AbstractImport : IImport
	{
		private List<string>	volumeDataPaths;
		private VolumeDatabase	targetDb;
		private string			dbDataPath;
		
		private volatile bool isRunning;
		private volatile bool importSucceeded;
		private volatile bool cancellationRequested;
		
		private double lastCompleted;
		
		private AsyncOperation asyncOperation;
		
		internal AbstractImport (VolumeDatabase targetDb, string dbDataPath) {
			this.volumeDataPaths = new List<string>();
			this.targetDb = targetDb;
			this.dbDataPath = dbDataPath;
			
			this.isRunning				= false;
			this.importSucceeded		= false;
			this.cancellationRequested	= false;
		}
		
		public WaitHandle RunAsync() {
			if (isRunning)
				throw new InvalidOperationException("Import is already running");

//			if (importSucceeded)
//				throw new InvalidOperationException("Import has been completed successfully. Create a new import");

			try {
				/* must be set (as soon as possible) in a function that is _not_ called asynchronously 
				 * (i.e. dont call it in ImportThread()) */
				isRunning = true;
				importSucceeded = false;
				cancellationRequested = false;
				asyncOperation = AsyncOperationManager.CreateOperation(null);

				Reset();
				
				/* invoke the import function on a new thread and return a waithandle */
				ImportThreadInvoker iti = new ImportThreadInvoker(ImportThread);
				IAsyncResult ar = iti.BeginInvoke(targetDb, dbDataPath, null, null);
				return ar.AsyncWaitHandle;
			
			} catch (Exception) {
				isRunning = false;

				if (asyncOperation != null)
					asyncOperation.OperationCompleted();

				throw;
			}
		}
		
		public void CancelAsync() {
			cancellationRequested = true;
		}

		public bool IsBusy {
			get { return isRunning; }
		}

		public bool ImportSucceeded {
			get { return importSucceeded; }
		}
		
		#region Events
		public event ErrorEventHandler				Error;
		public event ImportCompletedEventHandler	ImportCompleted;
		public event ProgressUpdateEventHandler		ProgressUpdate;
		#endregion
		
		protected void CheckForCancellationRequest() {
			if (cancellationRequested)
				throw new ImportCancelledException();
		}
		
		protected virtual void Reset() {
			lastCompleted = .0;
			volumeDataPaths.Clear();
		}
		
		protected static void Assert(bool condition, string msg) {
			if (!condition)
				throw new Exception("Assertion failed: " + msg);
		}
		
		protected abstract void ImportThreadMain(VolumeDatabase targetDb, string dbDataPath);
		
		private delegate void ImportThreadInvoker(VolumeDatabase targetDb, string dbDataPath);
		private void ImportThread(VolumeDatabase targetDb, string dbDataPath) {
			
			Exception fatalError = null;
			bool cancelled = false;
			
			try {
				
				targetDb.TransactionBegin(); // locks VolumeDatabase
				
				ImportThreadMain(targetDb, dbDataPath);
				
				targetDb.TransactionCommit(); // unlocks VolumeDatabase
				importSucceeded = true;
			
			} catch (Exception ex) {
				
				Exception cleanupException = null;
				try {
					targetDb.TransactionRollback();  // unlocks VolumeDatabase
					
					foreach (string path in volumeDataPaths)
						Directory.Delete(path, true);
				} catch (Exception e) {
					cleanupException = e;
				}
				
				if (ex is ImportCancelledException) {
					cancelled = true;
				} else {
					/* save the error that caused the import to stop (import failure) */
					fatalError = ex;
					PostError(ex);
					
					Debug.WriteLine("Details for exception in ImportThread():\n" + ex.ToString());
				}

				// in case an error occured while cleaning up, 
				// post the error here, _after_ the initial error that made the import fail.
				if (cleanupException != null) {
					PostError(cleanupException);
				}
				
//#if THROW_EXCEPTIONS_ON_ALL_THREADS
//				  if (!(ex is ScanCancelledException))
//					  throw;
//#endif
			} finally {
				isRunning = false;
				PostCompleted(fatalError, cancelled);
			}
		}
		
		protected string CreateThumbsDir(string dbDataPath, long volumeID) {
			string volumeDataPath = Path.Combine(dbDataPath, volumeID.ToString());
			volumeDataPaths.Add(volumeDataPath);
			
			// make sure there is no directory with the same name as the volume directory 
			// that is about to be created
			// (the volume directory will be deleted in the catch block on failure, 
			// so make sure that no existing dir will be deleted)
			if (Directory.Exists(volumeDataPath))
				throw new ArgumentException("dbDataPath already contains a directory for this volume");
			
			// thumbnails will be stored in <dbdataPath>/<volumeID>/thumbs
			string thumbnailPath = Path.Combine(volumeDataPath, "thumbs");
			Directory.CreateDirectory(thumbnailPath);
			
			return thumbnailPath;
		}
		
		protected static void ImportThumb(long sourceID,
		                                  long targetID,
		                                  string sourceThumbsPath,
		                                  string targetThumbsPath) {
			
			string sourceThumb = Path.Combine(sourceThumbsPath, sourceID.ToString() + ".png");
			string targetThumb = Path.Combine(targetThumbsPath, targetID.ToString() + ".png");
			
			if (File.Exists(sourceThumb))
				File.Copy(sourceThumb, targetThumb);
		}
		
		protected void PostProgressUpdate(double completed) {
			// update progress on every full percent point only
			// to save resources and cpu
			if (((int)completed - (int)lastCompleted) < 1)
				return;
			
			lastCompleted = completed;
			
			SendOrPostCallback cb = delegate(object args) {
				OnProgressUpdate((ProgressUpdateEventArgs)args);
			};

			ProgressUpdateEventArgs e = new ProgressUpdateEventArgs(completed);
			asyncOperation.Post(cb, e);
		}
		
		protected static T ReplaceDBNull<T>(object dbValue, T replaceValue) {
			return dbValue == DBNull.Value ? replaceValue : (T)dbValue;
		}
		
		/// <summary>
		/// Called when an unhandled exception occurs.
		/// </summary>
		private void PostError(Exception ex) {
			SendOrPostCallback cb = delegate(object args) {
				OnError((VolumeDB.Import.ErrorEventArgs)args);
			};

			ErrorEventArgs e = new ErrorEventArgs(ex);
			asyncOperation.Post(cb, e);
		}
		
		/// <summary>
		/// Called when importing has been completed.
		/// </summary>
		private void PostCompleted(Exception error, bool cancelled) {
			SendOrPostCallback cb = delegate(object args) {
				OnImportCompleted((ImportCompletedEventArgs)args);
			};

			ImportCompletedEventArgs e = new ImportCompletedEventArgs(error, cancelled);
			asyncOperation.PostOperationCompleted(cb, e);
		}
		
		private void OnProgressUpdate(ProgressUpdateEventArgs e) {
			if (this.ProgressUpdate != null)
				this.ProgressUpdate(this, e);
		}
		
		private void OnError(ErrorEventArgs e) {
			if (this.Error != null)
				this.Error(this, e);
		}

		private void OnImportCompleted(ImportCompletedEventArgs e) {
			if (this.ImportCompleted != null)
				this.ImportCompleted(this, e);
		}
		
		/// <summary>
		/// Signals that the import has been cancelled.
		/// </summary>
		protected class ImportCancelledException : Exception {
			public ImportCancelledException() : base() { }
		}
	}
}
