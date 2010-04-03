// AudioCdVolumeInfo.cs
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
using System.Threading;

namespace VolumeDB.VolumeScanner
{
	/* 
	 * Type returned by the VolumeInfo property of the AudioCdVolumeScanner class.
	 * It provides basic readonly info about the volume being scanned.
	 * Properties have to be threadsafe (client may read while scanner writes)).
	 */
	public class AudioCdVolumeInfo : VolumeInfo
	{
		// AudioCdVolumeScanner does write to these properties directly 
		// (e.g. via Interlocked.Increment())
		internal volatile int tracks;
		internal volatile int duration; // in seconds
		
		internal AudioCdVolumeInfo(AudioCdVolume v) : base(v) {
			this.tracks		= v.Tracks;
			this.duration	= v.Duration;
		}
		
		internal override void Reset () {
			Interlocked.Exchange(ref tracks, 0);
			Interlocked.Exchange(ref duration, 0);
		}
		
		public int Tracks	{ get { return tracks; } }
		public int Duration	{ get { return duration; } }
	}
}
