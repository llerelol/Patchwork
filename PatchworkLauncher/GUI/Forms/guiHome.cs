﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mono.Cecil.Cil;
using Patchwork.Attributes;
using Patchwork.Utility.Binding;

namespace PatchworkLauncher {

	public partial class guiHome : Form {

		public LaunchManager Manager {
			get;
			private set;
		}

		public guiHome(LaunchManager manager) {
			Manager = manager;
			InitializeComponent();
		}

		private void guiActiveMods_Click(object sender, EventArgs e) {
			LaunchManager.Instance.Command_OpenMods();
		}

		private void guiLaunchNoMods_Click(object sender, EventArgs e) {
			Manager.Command_Launch();
		}

		private void guiHome_Load(object sender, EventArgs e) {
			guiGameIcon.Image = Manager.ProgramIcon;
			guiPwVersion.Text = PatchworkVersion.Version;
			guiGameName.Text = Manager.AppInfo.AppName;
			guiGameVersion.Text = Manager.AppInfo.AppVersion;
			var isEnabled = Manager.State.Convert(x => x == LaunchManagerState.Idle);
			this.Bind(x => x.Enabled).Binding = isEnabled.ToBinding(BindingMode.IntoTarget);
			isEnabled.HasChanged += x => {
				if (x.Value) {
					Invoke((Action) (() => this.Focus()));
				}
			};
		}

		private void guiLaunchWithMods_Click(object sender, EventArgs e) {
			Manager.Command_Launch_Modded();
		}



		private void label2_Click(object sender, EventArgs e) {

		}

		private void guiChangeFolder_Click(object sender, EventArgs e) {
			Manager.Command_ChangeFolder();
		}

		private void guiTestRun_Click(object sender, EventArgs e)
		{
			Manager.Command_TestRun();
		}
	}
}