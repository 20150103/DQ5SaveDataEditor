using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
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
					var prev = this.val;
					this.val = value;

					// 袋のアイテム種類を変更したらアイテム名も更新
					if (this.isFukuroItem)
					{
						if (this.val == 0)
							this.Text = "未使用";
						else if (ItemCodes.ContainsKey(this.val))
							this.Text = ItemCodes[this.val];
						else
							this.Text = string.Empty;
					}
					else if (this.Pos >= POS_MONEY && this.Pos <= POS_MONEY + MONEY_SIZE)
					{
						updateMoney();
					}
					else if (this.isMonster)
					{
						if (MonsterCodes.ContainsKey(this.val))
							this.Text = MonsterCodes[this.val];
						else
							this.Text = string.Empty;
					}
					else if (this.IsString)
					{
						// 文字列に変換
						var bytes = BitConverter.GetBytes(this.Value);
						if (bytes[0] == 0 || bytes[0] == 0xD5) // よくわからん
							this.Text = string.Empty;
						else
							this.Text = Encoding.UTF8.GetString(bytes, 0, this.size);
					}

					// 差分を更新
					if (NeedToCalcDiff)
					{
						var diff = this.CalcDiff(prev, this.val);
						if (diff != 0)
						{
							Diff += diff;
							updateDiff(Diff);
						}
					}

					this.OnPropertyChanged("Text");
					this.OnPropertyChanged("Value");
				}
			}
			/* 意味なし
			/// <summary>
			/// 値の各バイトを10進表示
			/// サム調整支援
			/// </summary>
			public string ValueDec
			{
				get
				{
					var str = string.Empty;
					var bytes = BitConverter.GetBytes(this.val);
					for (var i = 0; i < this.size; i++)
						str += "{0}({1}) ".FormatEx(bytes[i], (int)(bytes[i] * Math.Pow(10, i)));
					return str;
				}
			}
			*/

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

			public string PosHex { get { return this.Pos.ToString("X4"); } }

			/// <summary>
			/// XORキー
			/// </summary>
			public byte[] Keys { get; set; }

			public Visibility DeleteButtonVisibility { get { return this.isFukuroItem ? Visibility.Visible : Visibility.Collapsed; } }

			/// <summary>
			/// 袋のアイテムか否か
			/// </summary>
			public bool isFukuroItem { get { return this.Pos >= POS_FUKURO_TYPE_S && this.Pos <= POS_FUKURO_TYPE_S + FUKURO_SIZE * FUKURO_TYPE_SIZE; } }

			/// <summary>
			/// モンスター種別名を表示するか否か
			/// </summary>
			public bool isMonster
			{
				get
				{
					if (this.Pos >= POS_MONSTER_S && this.Pos <= POS_MONSTER_S + MONSTER_SIZE * MONSTER_DATA_SIZE)
					{
						var mod = (this.Pos - POS_MONSTER_S) % MONSTER_DATA_SIZE;
						return mod == OFFSET_M_TYPE || mod == OFFSET_M_FACE;
					}
					return false;
				}
			}

			/// <summary>
			/// アイテムを表示するか否か
			/// </summary>
			public Visibility Visibility { get; set; }

			public static long Diff;

			/// <summary>
			/// 差分計算を実施するか否か
			/// 初期化時は不要
			/// </summary>
			public static bool NeedToCalcDiff = true;

			/// <summary>
			/// 文字列か否か
			/// </summary>
			public bool IsString { get; set; }

			/// <summary>
			/// 変更したい名称
			/// </summary>
			public string EditedName { get; set; }

			/// <summary>
			/// 名称エディタを表示するか否か
			/// 名称1にだけ表示する
			/// </summary>
			public Visibility NameEditorVisibility
			{
				get
				{
					if (this.Pos >= POS_MONSTER_S && this.Pos <= POS_MONSTER_S + MONSTER_SIZE * MONSTER_DATA_SIZE)
					{
						var mod = (this.Pos - POS_MONSTER_S) % MONSTER_DATA_SIZE;
						if (mod == OFFSET_M_NAME1)
							return Visibility.Visible;
					}
					return Visibility.Collapsed;
				}
			}

			#endregion

			public event PropertyChangedEventHandler PropertyChanged;

			/// <summary>
			/// 所持金更新
			/// </summary>
			public static Action updateMoney;

			/// <summary>
			/// 差分更新
			/// </summary>
			public static Action<long> updateDiff;

			/// <summary>
			/// 文字列変換
			/// </summary>
			public static Func<CData, string> getString;

			public CData()
			{
				this.Size = 1;
				this.Visibility = Visibility.Visible;
				this.IsString = false;
			}

			#region Public Functions

			public void OnPropertyChanged(string name)
			{
				if (PropertyChanged != null)
				{
					PropertyChanged(this, new PropertyChangedEventArgs(name));
				}
			}

			public override string ToString()
			{
				return "{0} {1:X2} {2:X2}".FormatEx(this.Title, this.Value0, this.Value);
			}

			public long CalcDiff(uint prev, uint next)
			{
				long diff = 0;
				var bytes0 = BitConverter.GetBytes(prev);
				var bytes = BitConverter.GetBytes(next);
				for (var i = 0; i < this.Size; i++)
				{
					diff += bytes[i];
					diff -= bytes0[i];
				}
				return diff;
			}

			#endregion
		}

		#endregion

		#region Consts

		/// <summary>
		/// アイテムコードファイル名
		/// フォーマット：コード(10進)、タブ、名称
		/// </summary>
		const string FILE_ITEM_CODES = "ItemCodes.txt";

		/// <summary>
		/// モンスターコードファイル名
		/// フォーマット：コード(10進)、タブ、名称
		/// </summary>
		const string FILE_MONSTER_CODES = "Monsters.txt";

		/// <summary>
		/// XORキーファイル
		/// フォーマット：アドレス(16進)、タブ、XORキー
		/// </summary>
		const string FILE_KEYS = "Keys.txt";

		/// <summary>
		/// 所持金の先頭アドレス
		/// </summary>
		const int POS_MONEY = 0x0014;

		/// <summary>
		/// 所持金のサイズ
		/// </summary>
		const int MONEY_SIZE = 4;

		/// <summary>
		/// 編集可能な袋アイテム数
		/// </summary>
		const int FUKURO_SIZE = 150;

		/// <summary>
		/// 袋のアイテム種類のサイズ
		/// </summary>
		const int FUKURO_TYPE_SIZE = 2;

		/// <summary>
		/// 袋のアイテム個数のサイズ
		/// </summary>
		const int FUKURO_COUNT_SIZE = 1;

		/// <summary>
		/// 袋アイテム種類の先頭アドレス
		/// </summary>
		const int POS_FUKURO_TYPE_S = 0x0034;

		/// <summary>
		/// 袋アイテム個数先頭アドレス
		/// </summary>
		const int POS_FUKURO_COUNT_S = 0x0258;

		/// <summary>
		/// モンスターの先頭アドレス(人間含む)
		/// </summary>
		const int POS_MONSTER_S = 0x0450; // 主人公？
		//const int POS_MONSTER_S = 0x0890; キラーパンサー

		/// <summary>
		/// 編集可能な仲間数(モンスター含む)
		/// </summary>
		const int MONSTER_SIZE = 100;

		/// <summary>
		/// モンスター1匹分のデータサイズ
		/// </summary>
		const int MONSTER_DATA_SIZE = 68;

		const int OFFSET_M_EXP = 0;						// 経験値

		const int OFFSET_M_CUR_HP = 4;					// 現在HP、以降ここからのオフセット
		const int OFFSET_M_MAX_HP = OFFSET_M_CUR_HP + 2;
		const int OFFSET_M_CUR_MP = OFFSET_M_CUR_HP + 4;
		const int OFFSET_M_MAX_MP = OFFSET_M_CUR_HP + 6;

		const int OFFSET_M_TYPE = OFFSET_M_CUR_HP + 8;	// 種別?
		const int OFFSET_M_RACE = OFFSET_M_CUR_HP + 34;	// 人間?(0=人間、1=モンスター?)
		const int OFFSET_M_FACE = OFFSET_M_CUR_HP + 35;	// 画像?

		const int OFFSET_M_NAME1 = OFFSET_M_CUR_HP + 36;	// 名前1文字目
		const int OFFSET_M_NAME2 = OFFSET_M_CUR_HP + 39;	// 名前2文字目
		const int OFFSET_M_NAME3 = OFFSET_M_CUR_HP + 42;	// 名前3文字目
		const int OFFSET_M_NAME4 = OFFSET_M_CUR_HP + 45;	// 名前4文字目

		const int OFFSET_M_STR = OFFSET_M_CUR_HP + 56;	// ちから
		const int OFFSET_M_DEF = OFFSET_M_CUR_HP + 57;	// みのまもり
		const int OFFSET_M_AGI = OFFSET_M_CUR_HP + 58;	// すばやさ
		const int OFFSET_M_WIT = OFFSET_M_CUR_HP + 59;	// かしこさ
		const int OFFSET_M_LUC = OFFSET_M_CUR_HP + 60;	// うんのよさ

		const int OFFSET_M_LV = OFFSET_M_EXP + MONSTER_DATA_SIZE - 3;	// レベル

		#endregion

		#region Fields

		/// <summary>
		/// 生データ
		/// </summary>
		byte[] data;

		/// <summary>
		/// アイテムコード表
		/// </summary>
		static Dictionary<uint, string> ItemCodes;

		/// <summary>
		/// モンスターコード表
		/// </summary>
		static Dictionary<uint, string> MonsterCodes;

		/// <summary>
		/// XORキー
		/// </summary>
		static Dictionary<int, byte> Keys;

		/// <summary>
		/// 全ての編集対象データ
		/// </summary>
		static List<CData> allItems;

		/// <summary>
		/// 編集対象データ(所持金、袋)
		/// </summary>
		static ObservableCollection<CData> Items;

		/// <summary>
		/// 編集対象データ(モンスター)
		/// </summary>
		static ObservableCollection<CData> Monsters;

		/// <summary>
		/// 袋アイテムの種類(番号順)
		/// </summary>
		static List<CData> fukuroTypes;

		/// <summary>
		/// 袋アイテムの個数(番号順)
		/// </summary>
		static List<CData> fukuroCounts;

		#endregion

		#region Constructors

		static MainWindow()
		{
			allItems = new List<CData>();
			ItemCodes = new Dictionary<uint, string>();
			MonsterCodes = new Dictionary<uint, string>();
			Keys = new Dictionary<int, byte>();

			#region アイテムコード一覧を読み取る

			var file = Path.Combine(Environment.CurrentDirectory, FILE_ITEM_CODES);
			if (File.Exists(file))
			{
				using (var sr = new StreamReader(file))
				{
					while (sr.Peek() > -1)
					{
						var line = sr.ReadLine();
						var values = line.Split('\t');
						if (values.Length == 2 && !string.IsNullOrEmpty(values[0]))
						{
							try
							{
								var val = Convert.ToUInt32(values[0]);
								var name = values[1];
								if (!ItemCodes.ContainsKey(val))
									ItemCodes.Add(val, name);
							}
							catch (Exception ex)
							{
								Console.WriteLine(ex);
							}
						}
					}
				}
			}

			#endregion

			#region モンスターコード一覧を読み取る

			file = Path.Combine(Environment.CurrentDirectory, FILE_MONSTER_CODES);
			if (File.Exists(file))
			{
				using (var sr = new StreamReader(file))
				{
					while (sr.Peek() > -1)
					{
						var line = sr.ReadLine();
						var values = line.Split('\t');
						if (values.Length >= 2 && !string.IsNullOrEmpty(values[0]))
						{
							try
							{
								var val = Convert.ToUInt32(values[0]);
								var name = values[1];
								if (!MonsterCodes.ContainsKey(val))
									MonsterCodes.Add(val, name);
							}
							catch (Exception ex)
							{
								Console.WriteLine(ex);
							}
						}
					}
				}
			}

			#endregion

			#region キー一覧を読み取る

			file = Path.Combine(Environment.CurrentDirectory, FILE_KEYS);
			if (File.Exists(file))
			{
				using (var sr = new StreamReader(file))
				{
					while (sr.Peek() > -1)
					{
						var line = sr.ReadLine();
						var values = line.Split('\t');
						if (values.Length > 1 && !string.IsNullOrEmpty(values[0]))
						{
							try
							{
								var pos = Convert.ToInt32(values[0], 16);
								var key = Convert.ToByte(values[1], 16);

								if (!Keys.ContainsKey(pos))
									Keys.Add(pos, key);
							}
							catch (Exception ex)
							{
								Console.WriteLine(ex);
							}
						}
					}
				}
			}

			#endregion

			#region 所持金、袋アイテムを初期化

			Items = new ObservableCollection<CData>();
			CData item = null;

			// - 所持金
			for (var i = 0; i < MONEY_SIZE; i++)
			{
				item = new CData();
				item.Title = "所持金{0}バイト目".FormatEx(i);
				item.Pos = POS_MONEY + i * item.Size;

				Items.Add(item);
				allItems.Add(item);
			}

			// - 袋
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
				allItems.Add(item);

				item = new CData();
				item.Title = "袋{0:D3}の個数".FormatEx(i + 1);
				item.Pos = POS_FUKURO_COUNT_S + i * item.Size;

				Items.Add(item);
				fukuroCounts.Add(item);
				allItems.Add(item);
			}

			#endregion

			#region 仲間、モンスターを初期化

			Monsters = new ObservableCollection<CData>();
			for (var i = 0; i < MONSTER_SIZE; i++)
			{
				var pos_head = POS_MONSTER_S + i * MONSTER_DATA_SIZE;

				// アドレス順に追加するほうが解析しやすい

				item = new CData();
				item.Title = "{0:D3}の経験値".FormatEx(i + 1);
				item.Size = 4;
				item.Pos = pos_head + OFFSET_M_EXP;

				Monsters.Add(item);
				allItems.Add(item);

				item = new CData();
				item.Title = "{0:D3}の現在HP".FormatEx(i + 1);
				item.Size = 2;
				item.Pos = pos_head + OFFSET_M_CUR_HP;

				Monsters.Add(item);
				allItems.Add(item);

				item = new CData();
				item.Title = "{0:D3}の最大HP".FormatEx(i + 1);
				item.Size = 2;
				item.Pos = pos_head + OFFSET_M_MAX_HP;

				Monsters.Add(item);
				allItems.Add(item);

				item = new CData();
				item.Title = "{0:D3}の現在MP".FormatEx(i + 1);
				item.Size = 2;
				item.Pos = pos_head + OFFSET_M_CUR_MP;

				Monsters.Add(item);
				allItems.Add(item);

				item = new CData();
				item.Title = "{0:D3}の最大MP".FormatEx(i + 1);
				item.Size = 2;
				item.Pos = pos_head + OFFSET_M_MAX_MP;

				Monsters.Add(item);
				allItems.Add(item);

				item = new CData();
				item.Title = "{0:D3}の種別?".FormatEx(i + 1);
				item.Size = 1;
				item.Pos = pos_head + OFFSET_M_TYPE;

				Monsters.Add(item);
				allItems.Add(item);

				item = new CData();
				item.Title = "{0:D3}の種族?".FormatEx(i + 1);
				item.Size = 1;
				item.Pos = pos_head + OFFSET_M_RACE;

				Monsters.Add(item);
				allItems.Add(item);

				item = new CData();
				item.Title = "{0:D3}の画像?".FormatEx(i + 1);
				item.Size = 1;
				item.Pos = pos_head + OFFSET_M_FACE;

				Monsters.Add(item);
				allItems.Add(item);

				item = new CData();
				item.Title = "{0:D3}の名前1?".FormatEx(i + 1);
				item.Size = 3;
				item.Pos = pos_head + OFFSET_M_NAME1;
				item.IsString = true;

				Monsters.Add(item);
				allItems.Add(item);

				item = new CData();
				item.Title = "{0:D3}の名前2?".FormatEx(i + 1);
				item.Size = 3;
				item.Pos = pos_head + OFFSET_M_NAME2;
				item.IsString = true;

				Monsters.Add(item);
				allItems.Add(item);

				item = new CData();
				item.Title = "{0:D3}の名前3?".FormatEx(i + 1);
				item.Size = 3;
				item.Pos = pos_head + OFFSET_M_NAME3;
				item.IsString = true;

				Monsters.Add(item);
				allItems.Add(item);

				item = new CData();
				item.Title = "{0:D3}の名前4?".FormatEx(i + 1);
				item.Size = 3;
				item.Pos = pos_head + OFFSET_M_NAME4;
				item.IsString = true;

				Monsters.Add(item);
				allItems.Add(item);

				item = new CData();
				item.Title = "{0:D3}のちから".FormatEx(i + 1);
				item.Size = 1;
				item.Pos = pos_head + OFFSET_M_STR;

				Monsters.Add(item);
				allItems.Add(item);

				item = new CData();
				item.Title = "{0:D3}のみのまもり".FormatEx(i + 1);
				item.Size = 1;
				item.Pos = pos_head + OFFSET_M_DEF;

				Monsters.Add(item);
				allItems.Add(item);

				item = new CData();
				item.Title = "{0:D3}のすばやさ".FormatEx(i + 1);
				item.Size = 1;
				item.Pos = pos_head + OFFSET_M_AGI;

				Monsters.Add(item);
				allItems.Add(item);

				item = new CData();
				item.Title = "{0:D3}のかしこさ".FormatEx(i + 1);
				item.Size = 1;
				item.Pos = pos_head + OFFSET_M_WIT;

				Monsters.Add(item);
				allItems.Add(item);

				item = new CData();
				item.Title = "{0:D3}のうんのよさ".FormatEx(i + 1);
				item.Size = 1;
				item.Pos = pos_head + OFFSET_M_LUC;

				Monsters.Add(item);
				allItems.Add(item);

				item = new CData();
				item.Title = "{0:D3}のレベル".FormatEx(i + 1);
				item.Size = 1;
				item.Pos = pos_head + OFFSET_M_LV;

				Monsters.Add(item);
				allItems.Add(item);
			}

			#endregion
		}

		public MainWindow()
		{
			InitializeComponent();

			this.lstData.ItemsSource = Items;			// 所持金、袋アイテム
			this.lstMonsters.ItemsSource = Monsters;	// 仲間、モンスター

			// 所持金更新用
			if (CData.updateMoney == null)
			{
				CData.updateMoney = () => this.txtMoney.Text = calcMoney().ToString();
			}

			// 差分更新用
			if (CData.updateDiff == null)
			{
				CData.updateDiff = (d) =>
					{
						var diff = d;
						var minus = diff < 0;

						if (minus)
							diff = diff * -1;

						this.txtDelta.Text = "{0}{1}".FormatEx(minus ? "-" : "+", diff);
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

					// 初期化
					this.ClearUIControls();
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
			// 差分チェック
			// - 計算してみる
			var diff = calcDiff();

			var msg = string.Empty;

			if (diff != CData.Diff)
				msg = "差分がおかしいかもしれないけれど、上書きしますか";
			else if (diff != 0)
				msg = "差分が0ではないけれど、上書きしますか";
			else
				msg = "上書きします";

			var dret = MessageBox.Show(msg, "確認", MessageBoxButton.OKCancel, MessageBoxImage.Question);
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

		/// <summary>
		/// 指定された名称を適用
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		void cmdUpdateName_Click(object sender, RoutedEventArgs e)
		{
			var btn = sender as Button;
			var head = btn.Tag as CData;
			var idx = Monsters.IndexOf(head);

			if (idx == -1) return;

			var name = head.EditedName;
			if (head.EditedName.Length > 4)
				name = head.EditedName.Substring(0, 4);

			// 1文字ずつ処理
			for (var i = 0; i < name.Length; i++)
			{
				var str = name.Substring(i, 1);

				var bytes = Encoding.UTF8.GetBytes(str);
				var item = Monsters[idx + i]; // 名前が順番に並んでいる前提
				
				uint val = 0;
				for (var j = 0; j < bytes.Length; j++)
				{
					val += (uint)(bytes[j] * Math.Pow(0x100, j));
				}
				item.Value = val;
				item.EditedName = string.Empty;
				item.OnPropertyChanged("EditedName");
			}
		}

		/// <summary>
		/// 差分の調整
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		void cmdAdjustDiff(object sender, RoutedEventArgs e)
		{
			var btn = sender as Button;
			var item = btn.Tag as CData;

			if (item != null)
			{
				if (CData.Diff > 0)
				{
					var bytes = BitConverter.GetBytes(item.Value);
					for (var i = 0; i < bytes.Length; i++)
					{
						var val = bytes[i];
						if (val > CData.Diff)
							item.Value -= (uint)(CData.Diff * Math.Pow(0x100, i));
						else
							item.Value -= (uint)(val * Math.Pow(0x100, i));
					}
				}
				else if (CData.Diff < 0)
				{
					var maxval = (uint)Math.Pow(0x100, item.Size) - 1; // このデータの理論上の最大値

					var bytes = BitConverter.GetBytes(item.Value);
					for (var i = 0; i < bytes.Length; i++)
					{
						var val = bytes[i];
						var ubound = byte.MaxValue - val; // 加算可能な上限
						var diff = -1 * CData.Diff;

						uint delta = 0;

						if (diff > ubound)
							delta = (uint)(ubound * Math.Pow(0x100, i));
						else
							delta = (uint)(diff * Math.Pow(0x100, i));

						if (item.Value + delta < maxval)
						{
							item.Value += delta;
						}
						else
						{
							item.Value = maxval;
							break;
						}
					}
				}
			}
		}

		#region 解析用

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

		void cmdDispData_Click(object sender, RoutedEventArgs e)
		{
			this.lstDebug.ItemsSource = null;
			var from = this.ToInt(this.txtFrom.Text, 16);
			var to = this.ToInt(this.txtTo.Text, 16);
			if (to >= this.data.Length)
				to = this.data.Length - 1;
			var size = this.ToInt(this.txtSize.Text);

			if (from >= 0 && to >= 0 && size >= 0)
			{
				var items = new ObservableCollection<CData>();
				CData.NeedToCalcDiff = false;

				var pos = from;
				while (pos <= to)
				{
					var item = new CData();
					item.Title = "調査中:{0:X4} ".FormatEx(pos);
					item.Size = size;
					item.Pos = pos;
					items.Add(item);

					pos += size;

					// XORキー
					if (Keys.ContainsKey(item.Pos))
					{
						for (var i = 0; i < item.Size; i++)
							item.Keys[i] = Keys[item.Pos + i];
					}

					// 値読み取り
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

					// 既に判明しているアドレスならば情報を付与
					var idx = allItems.FindIndex(ai => ai.Pos == item.Pos);
					if (idx != -1)
					{
						item.Text = allItems[idx].Title;
						item.OnPropertyChanged("Text");
					}
				}

				CData.NeedToCalcDiff = true;
				this.lstDebug.ItemsSource = items;
			}
		}

		void cmdFilter_Click(object sender, RoutedEventArgs e)
		{
			if (this.lstDebug.ItemsSource != null)
			{
				var from = this.ToInt(this.txtLower.Text);
				var to = this.ToInt(this.txtUpper.Text);
				if (to == -1)
					to = int.MaxValue;

				var items = this.lstDebug.ItemsSource as ObservableCollection<CData>;

				foreach (var item in items)
				{
					item.Visibility = item.Value >= from && item.Value <= to ? Visibility.Visible : Visibility.Collapsed;
					item.OnPropertyChanged("Visibility");
				}
			}
		}

		#endregion

		#endregion

		#region Private Functions

		// データ→表示
		void DataToUIControls()
		{
			foreach (var item in allItems)
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

				item.OnPropertyChanged("Value0");
			}
		}

		// 表示→データ
		void UIControlsToData()
		{
			foreach (var item in allItems)
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
			CData.NeedToCalcDiff = false;
			foreach (var item in allItems)
			{
				item.Value = item.Value0;
				item.OnPropertyChanged("Value");
			}
			CData.NeedToCalcDiff = true;

			// 差分
			CData.Diff = 0;
			this.txtDelta.Text = "0";
		}

		void ShowError(Exception ex)
		{
			var msg = "エラー発生{0}{1}".FormatEx(Environment.NewLine, ex);
			MessageBox.Show(msg, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
		}

		static int calcMoney()
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
			foreach (var item in allItems)
			{
				diff += item.CalcDiff(item.Value0, item.Value);
			}

			return diff;
		}

		int ToInt(string str, int fromBase = 10)
		{
			if (string.IsNullOrEmpty(str)) return -1;
			try
			{
				return Convert.ToInt32(str, fromBase);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}
			return -1;
		}

		#endregion
	}
}