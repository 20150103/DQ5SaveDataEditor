using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace DQ5SaveDataEditor
{
	/// <summary>
	/// スマホ版DQ5セーブデータエディタ
	/// - 所持金と袋のアイテム(種類、個数)を編集することが可能
	/// - サムの更新はしないので、変更値の合計が0になるように編集する必要がある
	/// - 変更値の合計が0以外の値の場合、サムチェックエラーになり、冒険の書が消える
	/// readme.txtみてね
	/// </summary>
	public partial class MainWindow : Window
	{
		#region Classes

		/// <summary>
		/// データモデル
		/// </summary>
		class CData : INotifyPropertyChanged
		{
			#region Properties

			/// <summary>
			/// 名称
			/// </summary>
			public string Title { get; set; }

			/// <summary>
			/// 初期値
			/// </summary>
			public uint Value0 { get; set; }

			private uint val;

			/// <summary>
			/// 値
			/// </summary>
			public uint Value
			{
				get { return val; }
				set
				{
					val = value;

					// 袋のアイテム種類を変更したらアイテム名も更新
					if (this.isFukuroItem)
					{
						if (val == 0)
							this.Text = "未使用";
						else if (ItemCodes.ContainsKey(val))
							this.Text = ItemCodes[val];
						else
							this.Text = string.Empty;
						this.OnPropertyChanged("Text");
					}
					else if (this.Pos >= POS_MONEY && this.Pos <= POS_MONEY + MONEY_SIZE)
					{
						updateMoney();
					}

					// 差分を更新
					updateDiff();

					this.OnPropertyChanged("Value");
				}
			}

			int size;

			/// <summary>
			/// 何バイト使用するか
			/// </summary>
			public int Size
			{
				get { return this.size; }
				set
				{
					this.size = value; this.Keys = new byte[value];
				}
			}

			/// <summary>
			/// 内容
			/// </summary>
			public string Text { get; set; }
			
			/// <summary>
			/// アドレス
			/// </summary>
			public int Pos { get; set; }

			/// <summary>
			/// XORキー
			/// </summary>
			public byte[] Keys { get; set; }

			public Visibility DeleteButtonVisibility { get { return this.isFukuroItem ? Visibility.Visible : Visibility.Collapsed; } }

			/// <summary>
			/// 袋のアイテムか否か
			/// </summary>
			public bool isFukuroItem { get { return this.Pos >= POS_FUKURO_TYPE_S && this.Pos <= POS_FUKURO_TYPE_S + FUKURO_SIZE * FUKURO_TYPE_SIZE; } }

			#endregion

			public event PropertyChangedEventHandler PropertyChanged;

			public void OnPropertyChanged(string name)
			{
				if (PropertyChanged != null)
				{
					PropertyChanged(this, new PropertyChangedEventArgs(name));
				}
			}

			public CData()
			{
				this.Size = 1;
			}

			public override string ToString()
			{
				return "{0} {1:X2} {2:X2}".FormatEx(this.Title, this.Value0, this.Value);
			}

			/// <summary>
			/// 所持金更新
			/// </summary>
			public static Action updateMoney;

			/// <summary>
			/// 差分更新
			/// </summary>
			public static Action updateDiff;
		}

		#endregion

		/// <summary>
		/// アイテムコードファイル名
		/// フォーマット：コード(10進)、タブ、名称
		/// </summary>
		const string FILE_ITEM_CODES = "ItemCodes.txt";

		/// <summary>
		/// XORキーファイル
		/// フォーマット：アドレス(16進)、タブ、XORキー
		/// </summary>
		const string FILE_KEYS = "Keys.txt";

		/// <summary>
		/// 所持金の先頭アドレス
		/// </summary>
		const int POS_MONEY = 0x0014;

		const int MONEY_SIZE = 4;

		/// <summary>
		/// 編集可能な袋アイテム数
		/// </summary>
		const int FUKURO_SIZE = 150;

		/// <summary>
		/// 袋のアイテム種類のサイズ
		/// </summary>
		const int FUKURO_TYPE_SIZE = 2;

		const int FUKURO_COUNT_SIZE = 1;

		/// <summary>
		/// 袋アイテム種類の先頭アドレス
		/// </summary>
		const int POS_FUKURO_TYPE_S = 0x0034;

		/// <summary>
		/// 袋アイテム個数先頭アドレス
		/// </summary>
		const int POS_FUKURO_COUNT_S = 0x0258;

		#region Fields

		/// <summary>
		/// 生データ
		/// </summary>
		byte[] data;
		
		/// <summary>
		/// 編集対象データ
		/// </summary>
		static ObservableCollection<CData> Items;

		/// <summary>
		/// アイテムコード表
		/// </summary>
		static Dictionary<uint, string> ItemCodes;

		/// <summary>
		/// XORキー
		/// </summary>
		static Dictionary<int, byte> Keys;

		static List<CData> fukuroTypes;

		static List<CData> fukuroCounts;

		#endregion

		#region Constructors

		static MainWindow()
		{
			ItemCodes = new Dictionary<uint, string>();
			Keys = new Dictionary<int, byte>();

			// アイテムコード一覧を読み取る
			var file = Path.Combine(Environment.CurrentDirectory, FILE_ITEM_CODES);
			if (File.Exists(file))
			{
				using (var sr = new StreamReader(file))
				{
					while (sr.Peek() > -1)
					{
						var line = sr.ReadLine();
						var values = line.Split('\t');
						if (values.Length == 2)
						{
							try
							{
								var val = Convert.ToUInt32(values[0]);
								var name = values[1];
								if (!ItemCodes.ContainsKey(val))
									ItemCodes.Add(val, name);
							}
							catch { }
						}
					}
				}
			}

			// キー一覧を読み取る
			file = Path.Combine(Environment.CurrentDirectory, FILE_KEYS);
			if (File.Exists(file))
			{
				using (var sr = new StreamReader(file))
				{
					while (sr.Peek() > -1)
					{
						var line = sr.ReadLine();
						var values = line.Split('\t');
						if (values.Length > 1)
						{
							try
							{
								var pos = Convert.ToInt32(values[0], 16);
								var key = Convert.ToByte(values[1], 16);

								if (!Keys.ContainsKey(pos))
									Keys.Add(pos, key);
							}
							catch { }
						}
					}
				}
			}

			// 編集対象アイテムを初期化
			Items = new ObservableCollection<CData>();

			CData item = null;

			// 所持金
			for (var i = 0; i < MONEY_SIZE; i++)
			{
				item = new CData();
				item.Title = "所持金iバイト目".FormatEx(i);
				item.Pos = POS_MONEY + i * item.Size;
				Items.Add(item);
			}

			// 袋
			fukuroTypes = new List<CData>();
			fukuroCounts = new List<CData>();

			for (var i = 0; i < FUKURO_SIZE; i++)
			{
				item = new CData();
				item.Title = "袋{0:D3}の種類".FormatEx(i + 1);
				item.Size = FUKURO_TYPE_SIZE;
				item.Pos = POS_FUKURO_TYPE_S + i * item.Size;
				Items.Add(item);
				fukuroTypes.Add(item);

				item = new CData();
				item.Title = "袋{0:D3}の個数".FormatEx(i + 1);
				item.Pos = POS_FUKURO_COUNT_S + i * item.Size;
				Items.Add(item);
				fukuroCounts.Add(item);
			}
		}

		public MainWindow()
		{
			InitializeComponent();

			this.lstData.ItemsSource = Items;

			// 所持金更新用
			if (CData.updateMoney == null)
			{
				CData.updateMoney = () => this.txtMoney.Text = CalcMoney().ToString();
			}

			// 差分更新用
			if (CData.updateDiff == null)
			{
				CData.updateDiff = () =>
					{
						var diff = calcDiff();
						var minus = diff < 0;

						if (minus)
							diff = diff * -1;

						this.txtDelta.Text = "{0}{1}".FormatEx(minus ? "-" : "+", diff);
						this.txtDeltaHex.Text = "{0}{1:X4}".FormatEx(minus ? "-" : "+", diff);
					};
			}
		}

		#endregion

		#region EventHandlers

		// データを開く
		void cmdLoadData(object sender, RoutedEventArgs e)
		{
			try
			{
				var wnd = new OpenFileDialog();
				if (true == wnd.ShowDialog())
				{
					var path = wnd.FileName;
					this.txtDataFilename.Text = path;

					// ファイル読み込み
					using (var fs = new FileStream(path, FileMode.Open))
					{
						this.data = new byte[fs.Length];
						fs.Read(this.data, 0, this.data.Length);
					}

					// データをコントロールに反映
					this.DataToUIControls();
				}
			}
			catch (Exception ex)
			{
				this.ShowError(ex);
			}
		}

		// データを保存
		void cmdSaveData(object sender, RoutedEventArgs e)
		{
			var dret = MessageBox.Show("上書きします", "確認",
				MessageBoxButton.OKCancel, MessageBoxImage.Question);
			if (dret == MessageBoxResult.OK)
			{
				try
				{
					this.UIControlsToData();
					using (var fs = new FileStream(this.txtDataFilename.Text, FileMode.Create))
					using (var bw = new BinaryWriter(fs))
					{
						foreach (var b in this.data)
							bw.Write(b);
					}
					MessageBox.Show("上書き完了", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
				}
				catch (Exception ex)
				{
					this.ShowError(ex);
				}
			}
		}

		/// <summary>
		/// データ書き出し
		/// 初期データを読み込み、FFでXORした値を出力する
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		void cmdOutputData(object sender, RoutedEventArgs e)
		{
			try
			{
				var file = Path.Combine(Environment.CurrentDirectory, FILE_KEYS);
				using (var sw = new StreamWriter(file, false))
				{
					for (var i = 0; i < this.data.Length; i++)
					{
						sw.WriteLine("{0:X4}\t{1:X2}", i, data[i] ^ 0xFF);
					}
				}
			}
			catch (Exception ex)
			{
				this.ShowError(ex);
			}
		}

		/// <summary>
		/// 変更を破棄
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		void cmdClear(object sender, RoutedEventArgs e)
		{
			this.ClearUIControls();
		}

		#endregion

		#region Private Functions

		// データ→表示
		void DataToUIControls()
		{
			foreach (var item in Items)
			{
				if (Keys.ContainsKey(item.Pos))
				{
					for (var i = 0; i < item.Size; i++)
						item.Keys[i] = Keys[item.Pos + i];
				}

				item.Value0 = 0;
				for (var i = 0; i < item.Size; i++)
				{
					var val = this.data[item.Pos + i];
					var xor = val ^ item.Keys[i];
					xor *= (int)Math.Pow(0x100, i);
					item.Value0 += (uint)xor;
				}

				item.Value = item.Value0;
				item.OnPropertyChanged("Value0");
				item.OnPropertyChanged("Value");
			}
		}

		// 表示→データ
		void UIControlsToData()
		{
			foreach (var item in Items)
			{
				// バイト配列に変換
				var bytes = BitConverter.GetBytes(item.Value);

				// XOR
				for (var i = 0; i < item.Size; i++)
					this.data[item.Pos + i] = (byte)(bytes[i] ^ item.Keys[i]);
			}
		}

		void ClearUIControls()
		{
			// 所持金
			this.txtMoney.Text = string.Empty;

			// データ
			foreach (var item in Items)
			{
				item.Value = item.Value0;
				item.OnPropertyChanged("Value");
			}
		}

		void ShowError(Exception ex)
		{
			var msg = "エラー発生{0}{1}".FormatEx(Environment.NewLine, ex);
			MessageBox.Show(msg, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
		}

		static int CalcMoney()
		{
			var sum = 0d;
			for (var i = 0; i < MONEY_SIZE; i++)
			{
				var pos = POS_MONEY + i;
				var val = Items.First(item => item.Pos == pos);
				sum += val.Value * Math.Pow(0x100, i);
			}

			return (int)sum;
		}

		static long calcDiff()
		{
			long diff = 0;
			foreach (var item in Items)
			{
				// バイト配列に変換して差分を計算する
				var bytes0 = BitConverter.GetBytes(item.Value0);
				var bytes = BitConverter.GetBytes(item.Value);

				for (var i = 0; i < item.Size; i++)
				{
					diff += bytes[i];
					diff -= bytes0[i];
				}
			}

			return diff;
		}

		#endregion

		/// <summary>
		/// 現在のアイテムを削除
		/// 以降のアイテムの位置を一つ前にずらす
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		void cmdDeleteItem_Click(object sender, RoutedEventArgs e)
		{
			var btn = sender as Button;
			var pos = (int)(btn.Tag);

			// 袋アイテム番号を取得
			var idx = fukuroTypes.FindIndex(f => f.Pos == pos);

			while (idx < fukuroTypes.Count - 1)
			{
				var type = fukuroTypes[idx];
				var count = fukuroCounts[idx];

				if (type.Value == 0 && count.Value == 0)
					break;

				++idx;
				type.Value = fukuroTypes[idx].Value;
				count.Value = fukuroCounts[idx].Value;
			}

			if (idx == fukuroTypes.Count - 1)
			{
				fukuroTypes[idx].Value = 0;
				fukuroCounts[idx].Value = 0;
			}
		}
	}
}

