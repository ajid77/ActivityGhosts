using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Dynastream.Fit;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using LemonUI;
using LemonUI.Menus;
using LiteDB;

namespace ActivityGhosts
{
    public class ActivityGhosts : Script
    {
        private readonly List<Ghost> ghosts;
        private Blip start;
        private int lastTime;
        private Keys menuKey;
        private Keys loadKey;
        public static PointF initialGPSPoint;
        public static int opacity;
        private bool showDate;
        private ObjectPool menuPool;
        private NativeMenu mainMenu;
        private NativeItem loadMenuItem;
        private NativeItem regroupMenuItem;
        private NativeItem deleteMenuItem;
        private readonly string gtaFolder;
        private static LiteDatabase db;
        private static ILiteCollection<ActivityFile> activityFiles;

        public ActivityGhosts()
        {
            ghosts = new List<Ghost>();
            lastTime = Environment.TickCount;
            gtaFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rockstar Games", "GTA V");
            db = new LiteDatabase(Path.Combine(gtaFolder, "ModSettings", "ActivityGhosts.db"));
            activityFiles = db.GetCollection<ActivityFile>();
            LoadSettings();
            CreateMenu();
            Tick += OnTick;
            Aborted += OnAbort;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (Environment.TickCount >= lastTime + 1000)
            {
                foreach (Ghost g in ghosts)
                    g.Update();
                lastTime = Environment.TickCount;
            }

            if (showDate)
                foreach (Ghost g in ghosts)
                    if (g.ped.IsOnScreen && g.ped.IsInRange(Game.Player.Character.Position, 20f))
                    {
                        var pos = g.ped.Bones[Bone.IKHead].Position + new Vector3(0, 0, 0.5f) + g.ped.Velocity / Game.FPS;
                        Function.Call(Hash.SET_DRAW_ORIGIN, pos.X, pos.Y, pos.Z, 0);
                        g.date.Scale = 0.4f - GameplayCamera.Position.DistanceTo(g.ped.Position) * 0.01f;
                        g.date.Draw();
                        Function.Call(Hash.CLEAR_DRAW_ORIGIN);
                    }
        }

        private void OnAbort(object sender, EventArgs e)
        {
            Tick -= OnTick;
            DeleteGhosts();
        }

        private void DeleteGhosts()
        {
            foreach (Ghost g in ghosts)
                g.Delete();
            ghosts.Clear();
            start?.Delete();
        }

        private void RegroupGhosts()
        {
            foreach (Ghost g in ghosts)
                g.Regroup(new PointF(Game.Player.Character.Position.X, Game.Player.Character.Position.Y));
            lastTime = Environment.TickCount;
        }

        private void LoadGhosts()
        {
            string activitiesPath = Path.Combine(gtaFolder, "Activities");
            if (Directory.Exists(activitiesPath))
            {
                foreach (var file in new DirectoryInfo(activitiesPath).GetFiles("*.fit"))
                    if (activityFiles.Find(x => x.Name == file.Name).FirstOrDefault() == null)
                    {
                        var points = new FitActivityDecoder(file.FullName).pointList;
                        if (points.Count > 1)
                            activityFiles.Insert(new ActivityFile() { Name = file.Name, Lat = points[0].Lat, Long = points[0].Long });
                    }
                foreach (var file in activityFiles.FindAll())
                    if (Game.Player.Character.Position.DistanceTo2D(new Vector2(file.Lat, file.Long)) < 50f)
                    {
                        string fullName = Path.Combine(activitiesPath, file.Name);
                        if (System.IO.File.Exists(fullName))
                        {
                            var fit = new FitActivityDecoder(fullName);
                            if (fit.pointList.Count > 1)
                            {
                                int offset = ghosts.Count / 2 + 1;
                                if (ghosts.Count % 2 == 0)
                                    offset *= -1;
                                float h = Game.Player.Character.Heading;
                                if ((h > 45f && h < 135f) || (h > 225f && h < 315f))
                                    fit.pointList[0].Long += offset;
                                else
                                    fit.pointList[0].Lat += offset;
                                ghosts.Add(new Ghost(fit.pointList, fit.sport, fit.startTime));
                            }
                        }
                        else
                            activityFiles.Delete(file.Id);
                    }
                if (ghosts.Count > 0)
                {
                    start = World.CreateBlip(Game.Player.Character.Position);
                    start.Sprite = BlipSprite.RaceBike;
                    loadMenuItem.Enabled = false;
                    regroupMenuItem.Enabled = true;
                    deleteMenuItem.Enabled = true;
                }
            }
            Notification.Show($"{ghosts.Count} ghosts loaded");
        }

        private void LoadSettings()
        {
            CultureInfo.CurrentCulture = new CultureInfo("", false);
            ScriptSettings settings = ScriptSettings.Load(@".\Scripts\ActivityGhosts.ini");
            menuKey = (Keys)Enum.Parse(typeof(Keys), settings.GetValue("Main", "MenuKey", "F8"), true);
            loadKey = (Keys)Enum.Parse(typeof(Keys), settings.GetValue("Main", "LoadKey", "G"), true);
            float initialGPSPointLat = settings.GetValue("Main", "InitialGPSPointLat", -19.10637f);
            float initialGPSPointLong = settings.GetValue("Main", "InitialGPSPointLong", -169.871f);
            initialGPSPoint = new PointF(initialGPSPointLat, initialGPSPointLong);
            opacity = settings.GetValue("Main", "Opacity", 5);
            if (opacity < 1) opacity = 1;
            if (opacity > 5) opacity = 5;
            opacity *= 51;
            showDate = settings.GetValue("Main", "ShowDate", true);
        }

        private void CreateMenu()
        {
            menuPool = new ObjectPool();
            mainMenu = new NativeMenu("ActivityGhosts");
            menuPool.Add(mainMenu);
            loadMenuItem = new NativeItem("Load", "Load ghosts")
            {
                Enabled = true
            };
            mainMenu.Add(loadMenuItem);
            loadMenuItem.Activated += (sender, itemArgs) =>
            {
                LoadGhosts();
                mainMenu.Visible = false;
            };
            regroupMenuItem = new NativeItem("Regroup", "Regroup ghosts")
            {
                Enabled = false
            };
            mainMenu.Add(regroupMenuItem);
            regroupMenuItem.Activated += (sender, itemArgs) =>
            {
                RegroupGhosts();
                mainMenu.Visible = false;
            };
            deleteMenuItem = new NativeItem("Delete", "Delete ghosts")
            {
                Enabled = false
            };
            mainMenu.Add(deleteMenuItem);
            deleteMenuItem.Activated += (sender, itemArgs) =>
            {
                DeleteGhosts();
                loadMenuItem.Enabled = true;
                regroupMenuItem.Enabled = false;
                deleteMenuItem.Enabled = false;
                mainMenu.Visible = false;
            };
            menuPool.RefreshAll();
            Tick += (o, e) => menuPool.Process();
            KeyDown += (o, e) =>
            {
                if (e.KeyCode == menuKey)
                    mainMenu.Visible = !mainMenu.Visible;
                else if (e.KeyCode == loadKey)
                {
                    if (ghosts.Count == 0)
                        LoadGhosts();
                    else
                        RegroupGhosts();
                }
            };
        }
    }

    public class Ghost
    {
        private readonly List<GeoPoint> points;
        private readonly Sport sport;
        private readonly Vehicle vehicle;
        public Ped ped;
        public TextElement date;
        private readonly Blip blip;
        private int index = 0;
        private bool finished = false;
        private readonly Animation animation = new Animation();
        private readonly Animation lastAnimation = new Animation();

        private readonly VehicleDrivingFlags customDrivingStyle = VehicleDrivingFlags.AllowGoingWrongWay |
                                                                  VehicleDrivingFlags.UseShortCutLinks |
                                                                  VehicleDrivingFlags.SteerAroundStationaryVehicles |
                                                                  VehicleDrivingFlags.SteerAroundObjects |
                                                                  VehicleDrivingFlags.SteerAroundPeds |
                                                                  VehicleDrivingFlags.SwerveAroundAllVehicles |
                                                                  VehicleDrivingFlags.ForceStraightLine;

        private readonly string[] availableBicycles = { "CRUISER", "FIXTER", "SCORCHER", "TRIBIKE", "TRIBIKE2", "TRIBIKE3" };

        private readonly string[] availableCyclists = {
            "a_f_m_beach_01",
            "a_f_m_bevhills_01",
            "a_f_m_fatwhite",
            "a_f_m_soucent_01",
            "a_f_m_soucentmc_01",
            "a_f_o_genstreet_01",
            "a_f_y_beach_01",
            "a_f_y_bevhills_01",
            "a_f_y_bevhills_02",
            "a_f_y_bevhills_03",
            "a_f_y_bevhills_04",
            "a_f_y_eastsa_01",
            "a_f_y_eastsa_02",
            "a_f_y_eastsa_03",
            "a_f_y_epsilon_01",
            "a_f_y_fitness_01",
            "a_f_y_fitness_02",
            "a_f_y_genhot_01",
            "a_f_y_golfer_01",
            "a_f_y_hiker_01",
            "a_f_y_hippie_01",
            "a_f_y_hipster_01",
            "a_f_y_hipster_02",
            "a_f_y_hipster_03",
            "a_f_y_hipster_04",
            "a_f_y_juggalo_01",
            "a_f_y_runner_01",
            "a_f_y_rurmeth_01",
            "a_f_y_scdressy_01",
            "a_f_y_skater_01",
            "a_f_y_soucent_01",
            "a_f_y_soucent_02",
            "a_f_y_soucent_03",
            "a_f_y_tennis_01",
            "a_f_y_topless_01",
            "a_f_y_tourist_01",
            "a_f_y_tourist_02",
            "a_f_y_vinewood_01",
            "a_f_y_vinewood_02",
            "a_f_y_vinewood_03",
            "a_f_y_vinewood_04",
            "a_f_y_yoga_01",
            "a_m_m_afriamer_01",
            "a_m_m_beach_01",
            "a_m_m_beach_02",
            "a_m_m_bevhills_01",
            "a_m_m_bevhills_02",
            "a_m_m_business_01",
            "a_m_m_eastsa_01",
            "a_m_m_eastsa_02",
            "a_m_m_farmer_01",
            "a_m_m_fatlatin_01",
            "a_m_m_genfat_01",
            "a_m_m_genfat_02",
            "a_m_m_golfer_01",
            "a_m_m_hasjew_01",
            "a_m_m_hillbilly_01",
            "a_m_m_hillbilly_02",
            "a_m_m_indian_01",
            "a_m_m_ktown_01",
            "a_m_m_malibu_01",
            "a_m_m_mexcntry_01",
            "a_m_m_mexlabor_01",
            "a_m_m_og_boss_01",
            "a_m_m_paparazzi_01",
            "a_m_m_polynesian_01",
            "a_m_m_prolhost_01",
            "a_m_m_rurmeth_01",
            "a_m_m_salton_01",
            "a_m_m_salton_02",
            "a_m_m_salton_03",
            "a_m_m_salton_04",
            "a_m_m_skater_01",
            "a_m_m_skidrow_01",
            "a_m_m_socenlat_01",
            "a_m_m_soucent_01",
            "a_m_m_soucent_02",
            "a_m_m_soucent_03",
            "a_m_m_soucent_04",
            "a_m_m_stlat_02",
            "a_m_m_tennis_01",
            "a_m_m_tourist_01",
            "a_m_m_tramp_01",
            "a_m_m_trampbeac_01",
            "a_m_m_trucker_01",
            "a_m_m_vincent_01",
            "a_m_m_yogacomm_01",
            "a_m_o_acult_01",
            "a_m_o_acult_02",
            "a_m_o_beach_01",
            "a_m_o_genstreet_01",
            "a_m_o_ktown_01",
            "a_m_o_salton_01",
            "a_m_o_soucent_01",
            "a_m_o_soucent_02",
            "a_m_o_soucent_03",
            "a_m_o_soucent_04",
            "a_m_y_beach_01",
            "a_m_y_beach_02",
            "a_m_y_beach_03",
            "a_m_y_beachvesp_01",
            "a_m_y_beachvesp_02",
            "a_m_y_beachvesp_03",
            "a_m_y_beachvesp_04",
            "a_m_y_bevhills_01",
            "a_m_y_bevhills_02",
            "a_m_y_bevhills_03",
            "a_m_y_bevhills_04",
            "a_m_y_busicas_01",
            "a_m_y_business_01",
            "a_m_y_business_02",
            "a_m_y_business_03",
            "a_m_y_clubcust_01",
            "a_m_y_clubcust_02",
            "a_m_y_clubcust_03",
            "a_m_y_cyclist_01",
            "a_m_y_dhill_01",
            "a_m_y_downtown_01",
            "a_m_y_eastsa_01",
            "a_m_y_eastsa_02",
            "a_m_y_epsilon_01",
            "a_m_y_epsilon_02",
            "a_m_y_gay_01",
            "a_m_y_gay_02",
            "a_m_y_genstreet_01",
            "a_m_y_genstreet_02",
            "a_m_y_golfer_01",
            "a_m_y_hasjew_01",
            "a_m_y_hiker_01",
            "a_m_y_hippy_01",
            "a_m_y_hipster_01",
            "a_m_y_hipster_02",
            "a_m_y_hipster_03",
            "a_m_y_hipster_04",
            "a_m_y_indian_01",
            "a_m_y_jetski_01",
            "a_m_y_juggalo_01",
            "a_m_y_ktown_01",
            "a_m_y_ktown_02",
            "a_m_y_latino_01",
            "a_m_y_methhead_01",
            "a_m_y_mexthug_01",
            "a_m_y_motox_01",
            "a_m_y_motox_02",
            "a_m_y_musclbeac_01",
            "a_m_y_musclbeac_02",
            "a_m_y_polynesian_01",
            "a_m_y_roadcyc_01",
            "a_m_y_runner_01",
            "a_m_y_runner_02",
            "a_m_y_salton_01",
            "a_m_y_skater_01",
            "a_m_y_skater_02",
            "a_m_y_soucent_01",
            "a_m_y_soucent_02",
            "a_m_y_soucent_03",
            "a_m_y_soucent_04",
            "a_m_y_stbla_01",
            "a_m_y_stbla_02",
            "a_m_y_stlat_01",
            "a_m_y_stwhi_01",
            "a_m_y_stwhi_02",
            "a_m_y_sunbathe_01",
            "a_m_y_surfer_01",
            "a_m_y_vindouche_01",
            "a_m_y_vinewood_01",
            "a_m_y_vinewood_02",
            "a_m_y_vinewood_03",
            "a_m_y_vinewood_04",
            "a_m_y_yoga_01",
            "cs_amandatownley",
            "cs_andreas",
            "cs_ashley",
            "cs_bankman",
            "cs_barry",
            "cs_beverly",
            "cs_brad",
            "cs_bradcadaver",
            "cs_carbuyer",
            "cs_casey",
            "cs_chengsr",
            "cs_chrisformage",
            "cs_clay",
            "cs_claypain",
            "cs_cletus",
            "cs_dale",
            "cs_davenorton",
            "cs_debra",
            "cs_denise",
            "cs_devin",
            "cs_dom",
            "cs_dreyfuss",
            "cs_drfriedlander",
            "cs_fabien",
            "cs_fbisuit_01",
            "cs_floyd",
            "cs_guadalope",
            "cs_gurk",
            "cs_hunter",
            "cs_janet",
            "cs_jewelass",
            "cs_jimmyboston",
            "cs_jimmydisanto",
            "cs_jiohnnyklebitz",
            "cs_josef",
            "cs_josh",
            "cs_karen_daniels",
            "cs_lamardavis",
            "cs_lazlow",
            "cs_lazlow_2",
            "cs_lestercrest",
            "cs_lifeinvad_01",
            "cs_magenta",
            "cs_manuel",
            "cs_marnie",
            "cs_martinmadrazo",
            "cs_maryann",
            "cs_michelle",
            "cs_milton",
            "cs_molly",
            "cs_movpremf_01",
            "cs_movpremmale",
            "cs_mrk",
            "cs_mrs_thornhill",
            "cs_mrsphillips",
            "cs_natalia",
            "cs_nervousron",
            "cs_nigel",
            "cs_old_man1a",
            "cs_old_man2",
            "cs_omega",
            "cs_orleans",
            "cs_paper",
            "cs_patricia",
            "cs_priest",
            "cs_prolsec_02",
            "cs_russiandrunk",
            "cs_siemonyetarian",
            "cs_solomon",
            "cs_stevehains",
            "cs_stretch",
            "cs_tanisha",
            "cs_taocheng",
            "cs_taostranslator",
            "cs_tenniscoach",
            "cs_terry",
            "cs_tom",
            "cs_tomepsilon",
            "cs_tracydisanto",
            "cs_wade",
            "cs_zimbor",
            "csb_abigail",
            "csb_anita",
            "csb_anton",
            "csb_ballasog",
            "csb_bride",
            "csb_burgerdrug",
            "csb_car3guy1",
            "csb_car3guy2",
            "csb_chef",
            "csb_chin_goon",
            "csb_cletus",
            "csb_cop",
            "csb_customer",
            "csb_denise_friend",
            "csb_fos_rep",
            "csb_g",
            "csb_groom",
            "csb_grove_str_dlr",
            "csb_hao",
            "csb_hugh",
            "csb_imran",
            "csb_janitor",
            "csb_maude",
            "csb_mweather",
            "csb_ortega",
            "csb_oscar",
            "csb_porndudes",
            "csb_prologuedriver",
            "csb_prolsec",
            "csb_ramp_gang",
            "csb_ramp_hic",
            "csb_ramp_hipster",
            "csb_ramp_marine",
            "csb_ramp_mex",
            "csb_reporter",
            "csb_roccopelosi",
            "csb_screen_writer",
            "csb_stripper_01",
            "csb_stripper_02",
            "csb_tonya",
            "csb_trafficwarden",
            "csb_undercover",
            "csb_vagspeak",
            "g_f_importexport_01",
            "g_f_y_ballas_01",
            "g_f_y_families_01",
            "g_f_y_lost_01",
            "g_f_y_vagos_01",
            "g_m_importexport_01",
            "g_m_m_armboss_01",
            "g_m_m_armgoon_01",
            "g_m_m_armlieut_01",
            "g_m_m_chemwork_01",
            "g_m_m_chiboss_01",
            "g_m_m_chicold_01",
            "g_m_m_chigoon_01",
            "g_m_m_chigoon_02",
            "g_m_m_korboss_01",
            "g_m_m_mexboss_01",
            "g_m_m_mexboss_02",
            "g_m_y_armgoon_02",
            "g_m_y_azteca_01",
            "g_m_y_ballaeast_01",
            "g_m_y_ballaorig_01",
            "g_m_y_ballasout_01",
            "g_m_y_famca_01",
            "g_m_y_famdnf_01",
            "g_m_y_famfor_01",
            "g_m_y_korean_01",
            "g_m_y_korean_02",
            "g_m_y_korlieut_01",
            "g_m_y_lost_01",
            "g_m_y_lost_02",
            "g_m_y_lost_03",
            "g_m_y_mexgang_01",
            "g_m_y_mexgoon_01",
            "g_m_y_mexgoon_02",
            "g_m_y_mexgoon_03",
            "g_m_y_pologoon_01",
            "g_m_y_pologoon_02",
            "g_m_y_salvaboss_01",
            "g_m_y_salvagoon_01",
            "g_m_y_salvagoon_02",
            "g_m_y_salvagoon_03",
            "g_m_y_strpunk_01",
            "g_m_y_strpunk_02",
            "hc_driver",
            "hc_gunman",
            "hc_hacker",
            "ig_abigail",
            "ig_agent",
            "ig_amandatownley",
            "ig_andreas",
            "ig_ashley",
            "ig_avon",
            "ig_ballasog",
            "ig_bankman",
            "ig_barry",
            "ig_benny",
            "ig_bestmen",
            "ig_beverly",
            "ig_brad",
            "ig_bride",
            "ig_car3guy1",
            "ig_car3guy2",
            "ig_casey",
            "ig_chef",
            "ig_chengsr",
            "ig_chrisformage",
            "ig_clay",
            "ig_claypain",
            "ig_cletus",
            "ig_dale",
            "ig_davenorton",
            "ig_denise",
            "ig_devin",
            "ig_dix",
            "ig_djblamadon",
            "ig_djblamrupert",
            "ig_djblamryanh",
            "ig_djdixmanager",
            "ig_djsolfotios",
            "ig_djsoljakob",
            "ig_djsolmanager",
            "ig_djsolmike",
            "ig_djsolrobt",
            "ig_djsoltony",
            "ig_dom",
            "ig_dreyfuss",
            "ig_drfriedlander",
            "ig_englishdave",
            "ig_fabien",
            "ig_fbisuit_01",
            "ig_floyd",
            "ig_g",
            "ig_groom",
            "ig_hao",
            "ig_hunter",
            "ig_isldj_00",
            "ig_isldj_01",
            "ig_isldj_02",
            "ig_isldj_03",
            "ig_isldj_04",
            "ig_janet",
            "ig_jay_norris",
            "ig_jewelass",
            "ig_jimmyboston",
            "ig_jimmydisanto",
            "ig_joeminuteman",
            "ig_johnnyklebitz",
            "ig_josef",
            "ig_josh",
            "ig_karen_daniels",
            "ig_kaylee",
            "ig_kerrymcintosh",
            "ig_lacey_jones_02",
            "ig_lamardavis",
            "ig_lazlow",
            "ig_lazlow_2",
            "ig_lestercrest",
            "ig_lifeinvad_01",
            "ig_lifeinvad_02",
            "ig_magenta",
            "ig_malc",
            "ig_manuel",
            "ig_marnie",
            "ig_maryann",
            "ig_maude",
            "ig_michelle",
            "ig_milton",
            "ig_molly",
            "ig_money",
            "ig_mp_agent14",
            "ig_mrk",
            "ig_mrs_thornhill",
            "ig_mrsphillips",
            "ig_natalia",
            "ig_nervousron",
            "ig_nigel",
            "ig_old_man1a",
            "ig_old_man2",
            "ig_omega",
            "ig_oneil",
            "ig_orleans",
            "ig_ortega",
            "ig_paige",
            "ig_paper",
            "ig_patricia",
            "ig_pilot",
            "ig_popov",
            "ig_priest",
            "ig_prolsec_02",
            "ig_ramp_gang",
            "ig_ramp_hic",
            "ig_ramp_hipster",
            "ig_ramp_mex",
            "ig_roccopelosi",
            "ig_russiandrunk",
            "ig_screen_writer",
            "ig_siemonyetarian",
            "ig_solomon",
            "ig_stevehains",
            "ig_stretch",
            "ig_talcc",
            "ig_tanisha",
            "ig_taocheng",
            "ig_taostranslator",
            "ig_tenniscoach",
            "ig_terry",
            "ig_tomepsilon",
            "ig_tonya",
            "ig_tracydisanto",
            "ig_trafficwarden",
            "ig_tylerdix",
            "ig_wade",
            "ig_zimbor",
            "mp_f_bennymech_01",
            "mp_f_boatstaff_01",
            "mp_f_cardesign_01",
            "mp_f_chbar_01",
            "mp_f_cocaine_01",
            "mp_f_counterfeit_01",
            "mp_f_deadhooker",
            "mp_f_execpa_01",
            "mp_f_execpa_02",
            "mp_f_forgery_01",
            "mp_f_helistaff_01",
            "mp_f_meth_01",
            "mp_f_misty_01",
            "mp_f_stripperlite",
            "mp_f_weed_01",
            "mp_g_m_pros_01",
            "mp_headtargets",
            "mp_m_avongoon",
            "mp_m_boatstaff_01",
            "mp_m_bogdangoon",
            "mp_m_claude_01",
            "mp_m_cocaine_01",
            "mp_m_counterfeit_01",
            "mp_m_exarmy_01",
            "mp_m_execpa_01",
            "mp_m_famdd_01",
            "mp_m_fibsec_01",
            "mp_m_forgery_01",
            "mp_m_freemode_01",
            "mp_m_g_vagfun_01",
            "mp_m_marston_01",
            "mp_m_meth_01",
            "mp_m_niko_01",
            "mp_m_securoguard_01",
            "mp_m_shopkeep_01",
            "mp_m_waremech_01",
            "mp_m_weapexp_01",
            "mp_m_weapwork_01",
            "mp_m_weed_01",
            "mp_s_m_armoured_01",
            "s_f_m_fembarber",
            "s_f_m_maid_01",
            "s_f_m_shop_high",
            "s_f_m_sweatshop_01",
            "s_f_y_airhostess_01",
            "s_f_y_bartender_01",
            "s_f_y_baywatch_01",
            "s_f_y_cop_01",
            "s_f_y_factory_01",
            "s_f_y_hooker_01",
            "s_f_y_hooker_02",
            "s_f_y_hooker_03",
            "s_f_y_migrant_01",
            "s_f_y_movprem_01",
            "s_f_y_ranger_01",
            "s_f_y_scrubs_01",
            "s_f_y_sheriff_01",
            "s_f_y_shop_low",
            "s_f_y_shop_mid",
            "s_f_y_stripper_01",
            "s_f_y_stripper_02",
            "s_f_y_stripperlite",
            "s_f_y_sweatshop_01",
            "s_m_m_ammucountry",
            "s_m_m_armoured_01",
            "s_m_m_armoured_02",
            "s_m_m_autoshop_01",
            "s_m_m_autoshop_02",
            "s_m_m_bouncer_01",
            "s_m_m_ccrew_01",
            "s_m_m_chemsec_01",
            "s_m_m_ciasec_01",
            "s_m_m_cntrybar_01",
            "s_m_m_dockwork_01",
            "s_m_m_doctor_01",
            "s_m_m_fiboffice_01",
            "s_m_m_fiboffice_02",
            "s_m_m_gaffer_01",
            "s_m_m_gardener_01",
            "s_m_m_gentransport",
            "s_m_m_hairdress_01",
            "s_m_m_highsec_01",
            "s_m_m_highsec_02",
            "s_m_m_janitor",
            "s_m_m_lathandy_01",
            "s_m_m_lifeinvad_01",
            "s_m_m_linecook",
            "s_m_m_lsmetro_01",
            "s_m_m_mariachi_01",
            "s_m_m_marine_01",
            "s_m_m_marine_02",
            "s_m_m_migrant_01",
            "s_m_m_movalien_01",
            "s_m_m_movprem_01",
            "s_m_m_movspace_01",
            "s_m_m_paramedic_01",
            "s_m_m_pilot_01",
            "s_m_m_pilot_02",
            "s_m_m_postal_01",
            "s_m_m_postal_02",
            "s_m_m_prisguard_01",
            "s_m_m_scientist_01",
            "s_m_m_security_01",
            "s_m_m_snowcop_01",
            "s_m_m_strperf_01",
            "s_m_m_strpreach_01",
            "s_m_m_strvend_01",
            "s_m_m_trucker_01",
            "s_m_m_ups_01",
            "s_m_m_ups_02",
            "s_m_o_busker_01",
            "s_m_y_airworker",
            "s_m_y_ammucity_01",
            "s_m_y_armymech_01",
            "s_m_y_autopsy_01",
            "s_m_y_barman_01",
            "s_m_y_baywatch_01",
            "s_m_y_blackops_01",
            "s_m_y_blackops_02",
            "s_m_y_blackops_03",
            "s_m_y_busboy_01",
            "s_m_y_chef_01",
            "s_m_y_clown_01",
            "s_m_y_construct_01",
            "s_m_y_construct_02",
            "s_m_y_cop_01",
            "s_m_y_dealer_01",
            "s_m_y_devinsec_01",
            "s_m_y_dockwork_01",
            "s_m_y_doorman_01",
            "s_m_y_dwservice_01",
            "s_m_y_dwservice_02",
            "s_m_y_factory_01",
            "s_m_y_fireman_01",
            "s_m_y_garbage",
            "s_m_y_grip_01",
            "s_m_y_hwaycop_01",
            "s_m_y_marine_01",
            "s_m_y_marine_02",
            "s_m_y_marine_03",
            "s_m_y_mime",
            "s_m_y_pestcont_01",
            "s_m_y_pilot_01",
            "s_m_y_prismuscl_01",
            "s_m_y_prisoner_01",
            "s_m_y_ranger_01",
            "s_m_y_robber_01",
            "s_m_y_shop_mask",
            "s_m_y_strvend_01",
            "s_m_y_swat_01",
            "s_m_y_uscg_01",
            "s_m_y_valet_01",
            "s_m_y_waiter_01",
            "s_m_y_winclean_01",
            "s_m_y_xmech_01",
            "s_m_y_xmech_02",
            "u_f_m_corpse_01",
            "u_f_m_drowned_01",
            "u_f_m_miranda",
            "u_f_m_promourn_01",
            "u_f_o_moviestar",
            "u_f_o_prolhost_01",
            "u_f_y_bikerchic",
            "u_f_y_comjane",
            "u_f_y_corpse_01",
            "u_f_y_corpse_02",
            "u_f_y_hotposh_01",
            "u_f_y_jewelass_01",
            "u_f_y_mistress",
            "u_f_y_poppymich",
            "u_f_y_princess",
            "u_f_y_spyactress",
            "u_m_m_aldinapoli",
            "u_m_m_bankman",
            "u_m_m_bikehire_01",
            "u_m_m_fibarchitect",
            "u_m_m_filmdirector",
            "u_m_m_glenstank_01",
            "u_m_m_griff_01",
            "u_m_m_jesus_01",
            "u_m_m_jewelsec_01",
            "u_m_m_jewelthief",
            "u_m_m_markfost",
            "u_m_m_partytarget",
            "u_m_m_prolsec_01",
            "u_m_m_promourn_01",
            "u_m_m_rivalpap",
            "u_m_m_spyactor",
            "u_m_m_willyfist",
            "u_m_o_filmnoir",
            "u_m_o_taphillbilly",
            "u_m_o_tramp_01",
            "u_m_y_abner",
            "u_m_y_antonb",
            "u_m_y_babyd",
            "u_m_y_baygor",
            "u_m_y_burgerdrug_01",
            "u_m_y_chip",
            "u_m_y_corpse_01",
            "u_m_y_cyclist_01",
            "u_m_y_fibmugger_01",
            "u_m_y_guido_01",
            "u_m_y_gunvend_01",
            "u_m_y_hippie_01",
            "u_m_y_imporage",
            "u_m_y_juggernaut_01",
            "u_m_y_justin",
            "u_m_y_mani",
            "u_m_y_militarybum",
            "u_m_y_paparazzi",
            "u_m_y_party_01",
            "u_m_y_pogo_01",
            "u_m_y_prisoner_01",
            "u_m_y_proldriver_01",
            "u_m_y_rsranger_01",
            "u_m_y_sbike",
            "u_m_y_smugmech_01",
            "u_m_y_staggrm_01",
            "u_m_y_tattoo_01",
            "u_m_y_zombie_01",
        };

        private readonly string[] availableRunners = {
            "a_f_m_beach_01",
            "a_f_m_bevhills_01",
            "a_f_m_fatwhite",
            "a_f_m_soucent_01",
            "a_f_m_soucentmc_01",
            "a_f_o_genstreet_01",
            "a_f_y_beach_01",
            "a_f_y_bevhills_01",
            "a_f_y_bevhills_02",
            "a_f_y_bevhills_03",
            "a_f_y_bevhills_04",
            "a_f_y_eastsa_01",
            "a_f_y_eastsa_02",
            "a_f_y_eastsa_03",
            "a_f_y_epsilon_01",
            "a_f_y_fitness_01",
            "a_f_y_fitness_02",
            "a_f_y_genhot_01",
            "a_f_y_golfer_01",
            "a_f_y_hiker_01",
            "a_f_y_hippie_01",
            "a_f_y_hipster_01",
            "a_f_y_hipster_02",
            "a_f_y_hipster_03",
            "a_f_y_hipster_04",
            "a_f_y_juggalo_01",
            "a_f_y_runner_01",
            "a_f_y_rurmeth_01",
            "a_f_y_scdressy_01",
            "a_f_y_skater_01",
            "a_f_y_soucent_01",
            "a_f_y_soucent_02",
            "a_f_y_soucent_03",
            "a_f_y_tennis_01",
            "a_f_y_topless_01",
            "a_f_y_tourist_01",
            "a_f_y_tourist_02",
            "a_f_y_vinewood_01",
            "a_f_y_vinewood_02",
            "a_f_y_vinewood_03",
            "a_f_y_vinewood_04",
            "a_f_y_yoga_01",
            "a_m_m_afriamer_01",
            "a_m_m_beach_01",
            "a_m_m_beach_02",
            "a_m_m_bevhills_01",
            "a_m_m_bevhills_02",
            "a_m_m_business_01",
            "a_m_m_eastsa_01",
            "a_m_m_eastsa_02",
            "a_m_m_farmer_01",
            "a_m_m_fatlatin_01",
            "a_m_m_genfat_01",
            "a_m_m_genfat_02",
            "a_m_m_golfer_01",
            "a_m_m_hasjew_01",
            "a_m_m_hillbilly_01",
            "a_m_m_hillbilly_02",
            "a_m_m_indian_01",
            "a_m_m_ktown_01",
            "a_m_m_malibu_01",
            "a_m_m_mexcntry_01",
            "a_m_m_mexlabor_01",
            "a_m_m_og_boss_01",
            "a_m_m_paparazzi_01",
            "a_m_m_polynesian_01",
            "a_m_m_prolhost_01",
            "a_m_m_rurmeth_01",
            "a_m_m_salton_01",
            "a_m_m_salton_02",
            "a_m_m_salton_03",
            "a_m_m_salton_04",
            "a_m_m_skater_01",
            "a_m_m_skidrow_01",
            "a_m_m_socenlat_01",
            "a_m_m_soucent_01",
            "a_m_m_soucent_02",
            "a_m_m_soucent_03",
            "a_m_m_soucent_04",
            "a_m_m_stlat_02",
            "a_m_m_tennis_01",
            "a_m_m_tourist_01",
            "a_m_m_tramp_01",
            "a_m_m_trampbeac_01",
            "a_m_m_trucker_01",
            "a_m_m_vincent_01",
            "a_m_m_yogacomm_01",
            "a_m_o_acult_01",
            "a_m_o_acult_02",
            "a_m_o_beach_01",
            "a_m_o_genstreet_01",
            "a_m_o_ktown_01",
            "a_m_o_salton_01",
            "a_m_o_soucent_01",
            "a_m_o_soucent_02",
            "a_m_o_soucent_03",
            "a_m_o_soucent_04",
            "a_m_y_beach_01",
            "a_m_y_beach_02",
            "a_m_y_beach_03",
            "a_m_y_beachvesp_01",
            "a_m_y_beachvesp_02",
            "a_m_y_beachvesp_03",
            "a_m_y_beachvesp_04",
            "a_m_y_bevhills_01",
            "a_m_y_bevhills_02",
            "a_m_y_bevhills_03",
            "a_m_y_bevhills_04",
            "a_m_y_busicas_01",
            "a_m_y_business_01",
            "a_m_y_business_02",
            "a_m_y_business_03",
            "a_m_y_clubcust_01",
            "a_m_y_clubcust_02",
            "a_m_y_clubcust_03",
            "a_m_y_cyclist_01",
            "a_m_y_dhill_01",
            "a_m_y_downtown_01",
            "a_m_y_eastsa_01",
            "a_m_y_eastsa_02",
            "a_m_y_epsilon_01",
            "a_m_y_epsilon_02",
            "a_m_y_gay_01",
            "a_m_y_gay_02",
            "a_m_y_genstreet_01",
            "a_m_y_genstreet_02",
            "a_m_y_golfer_01",
            "a_m_y_hasjew_01",
            "a_m_y_hiker_01",
            "a_m_y_hippy_01",
            "a_m_y_hipster_01",
            "a_m_y_hipster_02",
            "a_m_y_hipster_03",
            "a_m_y_hipster_04",
            "a_m_y_indian_01",
            "a_m_y_jetski_01",
            "a_m_y_juggalo_01",
            "a_m_y_ktown_01",
            "a_m_y_ktown_02",
            "a_m_y_latino_01",
            "a_m_y_methhead_01",
            "a_m_y_mexthug_01",
            "a_m_y_motox_01",
            "a_m_y_motox_02",
            "a_m_y_musclbeac_01",
            "a_m_y_musclbeac_02",
            "a_m_y_polynesian_01",
            "a_m_y_roadcyc_01",
            "a_m_y_runner_01",
            "a_m_y_runner_02",
            "a_m_y_salton_01",
            "a_m_y_skater_01",
            "a_m_y_skater_02",
            "a_m_y_soucent_01",
            "a_m_y_soucent_02",
            "a_m_y_soucent_03",
            "a_m_y_soucent_04",
            "a_m_y_stbla_01",
            "a_m_y_stbla_02",
            "a_m_y_stlat_01",
            "a_m_y_stwhi_01",
            "a_m_y_stwhi_02",
            "a_m_y_sunbathe_01",
            "a_m_y_surfer_01",
            "a_m_y_vindouche_01",
            "a_m_y_vinewood_01",
            "a_m_y_vinewood_02",
            "a_m_y_vinewood_03",
            "a_m_y_vinewood_04",
            "a_m_y_yoga_01",
            "cs_amandatownley",
            "cs_andreas",
            "cs_ashley",
            "cs_bankman",
            "cs_barry",
            "cs_beverly",
            "cs_brad",
            "cs_bradcadaver",
            "cs_carbuyer",
            "cs_casey",
            "cs_chengsr",
            "cs_chrisformage",
            "cs_clay",
            "cs_claypain",
            "cs_cletus",
            "cs_dale",
            "cs_davenorton",
            "cs_debra",
            "cs_denise",
            "cs_devin",
            "cs_dom",
            "cs_dreyfuss",
            "cs_drfriedlander",
            "cs_fabien",
            "cs_fbisuit_01",
            "cs_floyd",
            "cs_guadalope",
            "cs_gurk",
            "cs_hunter",
            "cs_janet",
            "cs_jewelass",
            "cs_jimmyboston",
            "cs_jimmydisanto",
            "cs_jiohnnyklebitz",
            "cs_josef",
            "cs_josh",
            "cs_karen_daniels",
            "cs_lamardavis",
            "cs_lazlow",
            "cs_lazlow_2",
            "cs_lestercrest",
            "cs_lifeinvad_01",
            "cs_magenta",
            "cs_manuel",
            "cs_marnie",
            "cs_martinmadrazo",
            "cs_maryann",
            "cs_michelle",
            "cs_milton",
            "cs_molly",
            "cs_movpremf_01",
            "cs_movpremmale",
            "cs_mrk",
            "cs_mrs_thornhill",
            "cs_mrsphillips",
            "cs_natalia",
            "cs_nervousron",
            "cs_nigel",
            "cs_old_man1a",
            "cs_old_man2",
            "cs_omega",
            "cs_orleans",
            "cs_paper",
            "cs_patricia",
            "cs_priest",
            "cs_prolsec_02",
            "cs_russiandrunk",
            "cs_siemonyetarian",
            "cs_solomon",
            "cs_stevehains",
            "cs_stretch",
            "cs_tanisha",
            "cs_taocheng",
            "cs_taostranslator",
            "cs_tenniscoach",
            "cs_terry",
            "cs_tom",
            "cs_tomepsilon",
            "cs_tracydisanto",
            "cs_wade",
            "cs_zimbor",
            "csb_abigail",
            "csb_anita",
            "csb_anton",
            "csb_ballasog",
            "csb_bride",
            "csb_burgerdrug",
            "csb_car3guy1",
            "csb_car3guy2",
            "csb_chef",
            "csb_chin_goon",
            "csb_cletus",
            "csb_cop",
            "csb_customer",
            "csb_denise_friend",
            "csb_fos_rep",
            "csb_g",
            "csb_groom",
            "csb_grove_str_dlr",
            "csb_hao",
            "csb_hugh",
            "csb_imran",
            "csb_janitor",
            "csb_maude",
            "csb_mweather",
            "csb_ortega",
            "csb_oscar",
            "csb_porndudes",
            "csb_prologuedriver",
            "csb_prolsec",
            "csb_ramp_gang",
            "csb_ramp_hic",
            "csb_ramp_hipster",
            "csb_ramp_marine",
            "csb_ramp_mex",
            "csb_reporter",
            "csb_roccopelosi",
            "csb_screen_writer",
            "csb_stripper_01",
            "csb_stripper_02",
            "csb_tonya",
            "csb_trafficwarden",
            "csb_undercover",
            "csb_vagspeak",
            "g_f_importexport_01",
            "g_f_y_ballas_01",
            "g_f_y_families_01",
            "g_f_y_lost_01",
            "g_f_y_vagos_01",
            "g_m_importexport_01",
            "g_m_m_armboss_01",
            "g_m_m_armgoon_01",
            "g_m_m_armlieut_01",
            "g_m_m_chemwork_01",
            "g_m_m_chiboss_01",
            "g_m_m_chicold_01",
            "g_m_m_chigoon_01",
            "g_m_m_chigoon_02",
            "g_m_m_korboss_01",
            "g_m_m_mexboss_01",
            "g_m_m_mexboss_02",
            "g_m_y_armgoon_02",
            "g_m_y_azteca_01",
            "g_m_y_ballaeast_01",
            "g_m_y_ballaorig_01",
            "g_m_y_ballasout_01",
            "g_m_y_famca_01",
            "g_m_y_famdnf_01",
            "g_m_y_famfor_01",
            "g_m_y_korean_01",
            "g_m_y_korean_02",
            "g_m_y_korlieut_01",
            "g_m_y_lost_01",
            "g_m_y_lost_02",
            "g_m_y_lost_03",
            "g_m_y_mexgang_01",
            "g_m_y_mexgoon_01",
            "g_m_y_mexgoon_02",
            "g_m_y_mexgoon_03",
            "g_m_y_pologoon_01",
            "g_m_y_pologoon_02",
            "g_m_y_salvaboss_01",
            "g_m_y_salvagoon_01",
            "g_m_y_salvagoon_02",
            "g_m_y_salvagoon_03",
            "g_m_y_strpunk_01",
            "g_m_y_strpunk_02",
            "hc_driver",
            "hc_gunman",
            "hc_hacker",
            "ig_abigail",
            "ig_agent",
            "ig_amandatownley",
            "ig_andreas",
            "ig_ashley",
            "ig_avon",
            "ig_ballasog",
            "ig_bankman",
            "ig_barry",
            "ig_benny",
            "ig_bestmen",
            "ig_beverly",
            "ig_brad",
            "ig_bride",
            "ig_car3guy1",
            "ig_car3guy2",
            "ig_casey",
            "ig_chef",
            "ig_chengsr",
            "ig_chrisformage",
            "ig_clay",
            "ig_claypain",
            "ig_cletus",
            "ig_dale",
            "ig_davenorton",
            "ig_denise",
            "ig_devin",
            "ig_dix",
            "ig_djblamadon",
            "ig_djblamrupert",
            "ig_djblamryanh",
            "ig_djdixmanager",
            "ig_djsolfotios",
            "ig_djsoljakob",
            "ig_djsolmanager",
            "ig_djsolmike",
            "ig_djsolrobt",
            "ig_djsoltony",
            "ig_dom",
            "ig_dreyfuss",
            "ig_drfriedlander",
            "ig_englishdave",
            "ig_fabien",
            "ig_fbisuit_01",
            "ig_floyd",
            "ig_g",
            "ig_groom",
            "ig_hao",
            "ig_hunter",
            "ig_isldj_00",
            "ig_isldj_01",
            "ig_isldj_02",
            "ig_isldj_03",
            "ig_isldj_04",
            "ig_janet",
            "ig_jay_norris",
            "ig_jewelass",
            "ig_jimmyboston",
            "ig_jimmydisanto",
            "ig_joeminuteman",
            "ig_johnnyklebitz",
            "ig_josef",
            "ig_josh",
            "ig_karen_daniels",
            "ig_kaylee",
            "ig_kerrymcintosh",
            "ig_lacey_jones_02",
            "ig_lamardavis",
            "ig_lazlow",
            "ig_lazlow_2",
            "ig_lestercrest",
            "ig_lifeinvad_01",
            "ig_lifeinvad_02",
            "ig_magenta",
            "ig_malc",
            "ig_manuel",
            "ig_marnie",
            "ig_maryann",
            "ig_maude",
            "ig_michelle",
            "ig_milton",
            "ig_molly",
            "ig_money",
            "ig_mp_agent14",
            "ig_mrk",
            "ig_mrs_thornhill",
            "ig_mrsphillips",
            "ig_natalia",
            "ig_nervousron",
            "ig_nigel",
            "ig_old_man1a",
            "ig_old_man2",
            "ig_omega",
            "ig_oneil",
            "ig_orleans",
            "ig_ortega",
            "ig_paige",
            "ig_paper",
            "ig_patricia",
            "ig_pilot",
            "ig_popov",
            "ig_priest",
            "ig_prolsec_02",
            "ig_ramp_gang",
            "ig_ramp_hic",
            "ig_ramp_hipster",
            "ig_ramp_mex",
            "ig_roccopelosi",
            "ig_russiandrunk",
            "ig_screen_writer",
            "ig_siemonyetarian",
            "ig_solomon",
            "ig_stevehains",
            "ig_stretch",
            "ig_talcc",
            "ig_tanisha",
            "ig_taocheng",
            "ig_taostranslator",
            "ig_tenniscoach",
            "ig_terry",
            "ig_tomepsilon",
            "ig_tonya",
            "ig_tracydisanto",
            "ig_trafficwarden",
            "ig_tylerdix",
            "ig_wade",
            "ig_zimbor",
            "mp_f_bennymech_01",
            "mp_f_boatstaff_01",
            "mp_f_cardesign_01",
            "mp_f_chbar_01",
            "mp_f_cocaine_01",
            "mp_f_counterfeit_01",
            "mp_f_deadhooker",
            "mp_f_execpa_01",
            "mp_f_execpa_02",
            "mp_f_forgery_01",
            "mp_f_helistaff_01",
            "mp_f_meth_01",
            "mp_f_misty_01",
            "mp_f_stripperlite",
            "mp_f_weed_01",
            "mp_g_m_pros_01",
            "mp_headtargets",
            "mp_m_avongoon",
            "mp_m_boatstaff_01",
            "mp_m_bogdangoon",
            "mp_m_claude_01",
            "mp_m_cocaine_01",
            "mp_m_counterfeit_01",
            "mp_m_exarmy_01",
            "mp_m_execpa_01",
            "mp_m_famdd_01",
            "mp_m_fibsec_01",
            "mp_m_forgery_01",
            "mp_m_freemode_01",
            "mp_m_g_vagfun_01",
            "mp_m_marston_01",
            "mp_m_meth_01",
            "mp_m_niko_01",
            "mp_m_securoguard_01",
            "mp_m_shopkeep_01",
            "mp_m_waremech_01",
            "mp_m_weapexp_01",
            "mp_m_weapwork_01",
            "mp_m_weed_01",
            "mp_s_m_armoured_01",
            "s_f_m_fembarber",
            "s_f_m_maid_01",
            "s_f_m_shop_high",
            "s_f_m_sweatshop_01",
            "s_f_y_airhostess_01",
            "s_f_y_bartender_01",
            "s_f_y_baywatch_01",
            "s_f_y_cop_01",
            "s_f_y_factory_01",
            "s_f_y_hooker_01",
            "s_f_y_hooker_02",
            "s_f_y_hooker_03",
            "s_f_y_migrant_01",
            "s_f_y_movprem_01",
            "s_f_y_ranger_01",
            "s_f_y_scrubs_01",
            "s_f_y_sheriff_01",
            "s_f_y_shop_low",
            "s_f_y_shop_mid",
            "s_f_y_stripper_01",
            "s_f_y_stripper_02",
            "s_f_y_stripperlite",
            "s_f_y_sweatshop_01",
            "s_m_m_ammucountry",
            "s_m_m_armoured_01",
            "s_m_m_armoured_02",
            "s_m_m_autoshop_01",
            "s_m_m_autoshop_02",
            "s_m_m_bouncer_01",
            "s_m_m_ccrew_01",
            "s_m_m_chemsec_01",
            "s_m_m_ciasec_01",
            "s_m_m_cntrybar_01",
            "s_m_m_dockwork_01",
            "s_m_m_doctor_01",
            "s_m_m_fiboffice_01",
            "s_m_m_fiboffice_02",
            "s_m_m_gaffer_01",
            "s_m_m_gardener_01",
            "s_m_m_gentransport",
            "s_m_m_hairdress_01",
            "s_m_m_highsec_01",
            "s_m_m_highsec_02",
            "s_m_m_janitor",
            "s_m_m_lathandy_01",
            "s_m_m_lifeinvad_01",
            "s_m_m_linecook",
            "s_m_m_lsmetro_01",
            "s_m_m_mariachi_01",
            "s_m_m_marine_01",
            "s_m_m_marine_02",
            "s_m_m_migrant_01",
            "s_m_m_movalien_01",
            "s_m_m_movprem_01",
            "s_m_m_movspace_01",
            "s_m_m_paramedic_01",
            "s_m_m_pilot_01",
            "s_m_m_pilot_02",
            "s_m_m_postal_01",
            "s_m_m_postal_02",
            "s_m_m_prisguard_01",
            "s_m_m_scientist_01",
            "s_m_m_security_01",
            "s_m_m_snowcop_01",
            "s_m_m_strperf_01",
            "s_m_m_strpreach_01",
            "s_m_m_strvend_01",
            "s_m_m_trucker_01",
            "s_m_m_ups_01",
            "s_m_m_ups_02",
            "s_m_o_busker_01",
            "s_m_y_airworker",
            "s_m_y_ammucity_01",
            "s_m_y_armymech_01",
            "s_m_y_autopsy_01",
            "s_m_y_barman_01",
            "s_m_y_baywatch_01",
            "s_m_y_blackops_01",
            "s_m_y_blackops_02",
            "s_m_y_blackops_03",
            "s_m_y_busboy_01",
            "s_m_y_chef_01",
            "s_m_y_clown_01",
            "s_m_y_construct_01",
            "s_m_y_construct_02",
            "s_m_y_cop_01",
            "s_m_y_dealer_01",
            "s_m_y_devinsec_01",
            "s_m_y_dockwork_01",
            "s_m_y_doorman_01",
            "s_m_y_dwservice_01",
            "s_m_y_dwservice_02",
            "s_m_y_factory_01",
            "s_m_y_fireman_01",
            "s_m_y_garbage",
            "s_m_y_grip_01",
            "s_m_y_hwaycop_01",
            "s_m_y_marine_01",
            "s_m_y_marine_02",
            "s_m_y_marine_03",
            "s_m_y_mime",
            "s_m_y_pestcont_01",
            "s_m_y_pilot_01",
            "s_m_y_prismuscl_01",
            "s_m_y_prisoner_01",
            "s_m_y_ranger_01",
            "s_m_y_robber_01",
            "s_m_y_shop_mask",
            "s_m_y_strvend_01",
            "s_m_y_swat_01",
            "s_m_y_uscg_01",
            "s_m_y_valet_01",
            "s_m_y_waiter_01",
            "s_m_y_winclean_01",
            "s_m_y_xmech_01",
            "s_m_y_xmech_02",
            "u_f_m_corpse_01",
            "u_f_m_drowned_01",
            "u_f_m_miranda",
            "u_f_m_promourn_01",
            "u_f_o_moviestar",
            "u_f_o_prolhost_01",
            "u_f_y_bikerchic",
            "u_f_y_comjane",
            "u_f_y_corpse_01",
            "u_f_y_corpse_02",
            "u_f_y_hotposh_01",
            "u_f_y_jewelass_01",
            "u_f_y_mistress",
            "u_f_y_poppymich",
            "u_f_y_princess",
            "u_f_y_spyactress",
            "u_m_m_aldinapoli",
            "u_m_m_bankman",
            "u_m_m_bikehire_01",
            "u_m_m_fibarchitect",
            "u_m_m_filmdirector",
            "u_m_m_glenstank_01",
            "u_m_m_griff_01",
            "u_m_m_jesus_01",
            "u_m_m_jewelsec_01",
            "u_m_m_jewelthief",
            "u_m_m_markfost",
            "u_m_m_partytarget",
            "u_m_m_prolsec_01",
            "u_m_m_promourn_01",
            "u_m_m_rivalpap",
            "u_m_m_spyactor",
            "u_m_m_willyfist",
            "u_m_o_filmnoir",
            "u_m_o_taphillbilly",
            "u_m_o_tramp_01",
            "u_m_y_abner",
            "u_m_y_antonb",
            "u_m_y_babyd",
            "u_m_y_baygor",
            "u_m_y_burgerdrug_01",
            "u_m_y_chip",
            "u_m_y_corpse_01",
            "u_m_y_cyclist_01",
            "u_m_y_fibmugger_01",
            "u_m_y_guido_01",
            "u_m_y_gunvend_01",
            "u_m_y_hippie_01",
            "u_m_y_imporage",
            "u_m_y_juggernaut_01",
            "u_m_y_justin",
            "u_m_y_mani",
            "u_m_y_militarybum",
            "u_m_y_paparazzi",
            "u_m_y_party_01",
            "u_m_y_pogo_01",
            "u_m_y_prisoner_01",
            "u_m_y_proldriver_01",
            "u_m_y_rsranger_01",
            "u_m_y_sbike",
            "u_m_y_smugmech_01",
            "u_m_y_staggrm_01",
            "u_m_y_tattoo_01",
            "u_m_y_zombie_01",
        };

        public Ghost(List<GeoPoint> pointList, Sport type, System.DateTime startTime)
        {
            points = pointList;
            sport = type;
            Random random = new Random();
            Vector3 start = GetPoint(index);
            if (sport == Sport.Cycling)
            {
                Model vModel;
                vModel = new Model(availableBicycles[random.Next(availableBicycles.Length)]);
                vModel.Request();
                if (vModel.IsInCdImage && vModel.IsValid)
                {
                    while (!vModel.IsLoaded)
                        Script.Wait(10);
                    vehicle = World.CreateVehicle(vModel, start);
                    vModel.MarkAsNoLongerNeeded();
                    vehicle.IsInvincible = true;
                    vehicle.Opacity = ActivityGhosts.opacity;
                    vehicle.Mods.CustomPrimaryColor = Color.FromArgb(random.Next(255), random.Next(255), random.Next(255));
                }
            }
            Model pModel;
            pModel = sport == Sport.Cycling ? new Model(availableCyclists[random.Next(availableCyclists.Length)]) :
                new Model(availableRunners[random.Next(availableRunners.Length)]);
            pModel.Request();
            if (pModel.IsInCdImage && pModel.IsValid)
            {
                while (!pModel.IsLoaded)
                    Script.Wait(10);
                ped = World.CreatePed(pModel, start);
                pModel.MarkAsNoLongerNeeded();
                ped.IsInvincible = true;
                ped.Opacity = ActivityGhosts.opacity;
                if (sport == Sport.Cycling)
                {
                    ped.SetIntoVehicle(vehicle, VehicleSeat.Driver);
                    vehicle.Heading = GetHeading(index);
                }
                else
                    ped.Heading = GetHeading(index);
                blip = ped.AddBlip();
                blip.Sprite = BlipSprite.Ghost;
                blip.Name = "Ghost (active)";
                blip.Color = BlipColor.WhiteNotPure;
            }
            date = new TextElement(TimeSince(startTime), new PointF(0, 0), 1f, Color.WhiteSmoke, GTA.UI.Font.ChaletLondon, Alignment.Center, false, true);
        }

        public void Update()
        {
            if (points.Count > index + 1)
            {
                float speed = points[index].Speed;
                if (sport == Sport.Cycling)
                {
                    if (!ped.IsInVehicle(vehicle))
                        ped.SetIntoVehicle(vehicle, VehicleSeat.Driver);
                    float distance = vehicle.Position.DistanceTo2D(GetPoint(index));
                    if (distance > 20f)
                    {
                        vehicle.Position = GetPoint(index);
                        vehicle.Heading = GetHeading(index);
                    }
                    else if (distance > 5f)
                        speed *= 1.1f;
                    index++;
                    ped.Task.ClearAll();
                    ped.Task.DriveTo(vehicle, GetPoint(index), 0f, speed, (DrivingStyle)customDrivingStyle);
                    vehicle.Speed = speed;
                }
                else
                {
                    float distance = ped.Position.DistanceTo2D(GetPoint(index));
                    if (distance > 10f)
                    {
                        ped.Position = GetPoint(index);
                        ped.Heading = GetHeading(index);
                    }
                    else if (distance > 3f)
                        speed *= 1.1f;
                    index++;
                    ped.Task.GoTo(GetPoint(index));
                    SetAnimation(speed);
                    ped.Speed = speed;
                }
            }
            else if (!finished)
            {
                finished = true;
                ped.Task.ClearAll();
                if (sport == Sport.Cycling && ped.IsInVehicle(vehicle))
                    ped.Task.LeaveVehicle(vehicle, false);
                blip.Name = "Ghost (finished)";
                blip.Color = BlipColor.Red;
            }
        }

        public void Regroup(PointF point)
        {
            index = points.IndexOf(points.OrderBy(x => Distance(point, x)).First());
            if (points.Count > index + 1)
            {
                if (finished)
                {
                    finished = false;
                    blip.Name = "Ghost (active)";
                    blip.Color = BlipColor.WhiteNotPure;
                }
                if (sport == Sport.Cycling)
                {
                    if (!ped.IsInVehicle(vehicle))
                        ped.SetIntoVehicle(vehicle, VehicleSeat.Driver);
                    vehicle.Position = GetPoint(index);
                    vehicle.Heading = GetHeading(index);
                    ped.Task.ClearAll();
                    ped.Task.DriveTo(vehicle, GetPoint(index + 1), 0f, points[index].Speed, (DrivingStyle)customDrivingStyle);
                    vehicle.Speed = points[index].Speed;
                }
                else
                {
                    ped.Position = GetPoint(index);
                    ped.Heading = GetHeading(index);
                    ped.Task.GoTo(GetPoint(index + 1));
                    SetAnimation(points[index].Speed);
                    ped.Speed = points[index].Speed;
                }
                index++;
            }
        }

        private double Distance(PointF from, GeoPoint to)
        {
            return Math.Sqrt((to.Long - from.Y) * (to.Long - from.Y) + (to.Lat - from.X) * (to.Lat - from.X));
        }

        private Vector3 GetPoint(int i)
        {
            return new Vector3(points[i].Lat, points[i].Long, World.GetGroundHeight(new Vector2(points[i].Lat, points[i].Long)));
        }

        private float GetHeading(int i)
        {
            return (new Vector2(points[i + 1].Lat, points[i + 1].Long) - new Vector2(points[i].Lat, points[i].Long)).ToHeading();
        }

        public void Delete()
        {
            blip.Delete();
            ped.Delete();
            vehicle?.Delete();
            points.Clear();
        }

        private class Animation
        {
            public string dictionary;
            public string name;
            public float speed;

            public Animation()
            {
                dictionary = "";
                name = "";
                speed = 0.0f;
            }

            public bool IsEmpty()
            {
                return dictionary == "" && name == "" && speed == 0.0f;
            }
        }

        private void SetAnimation(float speed)
        {
            if (speed < 2.4f)
            {
                animation.dictionary = "move_m@casual@f";
                animation.name = "walk";
                animation.speed = speed / 1.69f;
            }
            else if (speed >= 2.4f && speed < 4.6f)
            {
                animation.dictionary = "move_m@jog@";
                animation.name = "run";
                animation.speed = speed / 3.13f;
            }
            else if (speed >= 4.6f)
            {
                animation.dictionary = "move_m@gangster@generic";
                animation.name = "sprint";
                animation.speed = speed / 6.63f;
            }
            if (animation.name != lastAnimation.name || ped.Speed == 0)
            {
                if (!lastAnimation.IsEmpty())
                    ped.Task.ClearAnimation(lastAnimation.dictionary, lastAnimation.name);
                ped.Task.PlayAnimation(animation.dictionary, animation.name, 8.0f, -8.0f, -1,
                    AnimationFlags.Loop | AnimationFlags.Secondary, animation.speed);
                lastAnimation.dictionary = animation.dictionary;
                lastAnimation.name = animation.name;
            }
            Function.Call(Hash.SET_ENTITY_ANIM_SPEED, ped, animation.dictionary, animation.name, animation.speed);
        }

        private string TimeSince(System.DateTime startTime)
        {
            var seconds = (System.DateTime.UtcNow - startTime).TotalSeconds;
            var interval = Math.Floor(seconds / 31536000);
            string intervalType;
            if (interval > 0) intervalType = "year";
            else
            {
                interval = Math.Floor(seconds / 2592000);
                if (interval > 0) intervalType = "month";
                else
                {
                    interval = Math.Floor(seconds / 604800);
                    if (interval > 0) intervalType = "week";
                    else
                    {
                        interval = Math.Floor(seconds / 86400);
                        if (interval > 0) intervalType = "day";
                        else
                        {
                            interval = Math.Floor(seconds / 3600);
                            if (interval > 0) intervalType = "hour";
                            else
                            {
                                interval = Math.Floor(seconds / 60);
                                if (interval > 0) intervalType = "minute";
                                else return "Just now";
                            }
                        }
                    }
                }
            }
            return $"{interval} {intervalType}{(interval > 1 ? "s" : "")} ago";
        }
    }

    public class GeoPoint
    {
        public float Lat;
        public float Long;
        public float Speed;

        public GeoPoint(float lat, float lon, float speed)
        {
            Lat = lat;
            Long = lon;
            Speed = speed;
        }
    }

    public class FitActivityDecoder
    {
        public List<GeoPoint> pointList;
        public System.DateTime startTime;
        public Sport sport = Sport.Cycling;

        public FitActivityDecoder(string fileName)
        {
            pointList = new List<GeoPoint>();
            startTime = new FileInfo(fileName).CreationTime;
            var fitSource = new FileStream(fileName, FileMode.Open);
            using (fitSource)
            {
                Decode decode = new Decode();
                MesgBroadcaster mesgBroadcaster = new MesgBroadcaster();
                decode.MesgEvent += mesgBroadcaster.OnMesg;
                decode.MesgDefinitionEvent += mesgBroadcaster.OnMesgDefinition;
                mesgBroadcaster.RecordMesgEvent += OnRecordMessage;
                mesgBroadcaster.SessionMesgEvent += OnSessionMessage;
                bool status = decode.IsFIT(fitSource);
                status &= decode.CheckIntegrity(fitSource);
                if (status)
                    decode.Read(fitSource);
                fitSource.Close();
            }
        }

        private void OnRecordMessage(object sender, MesgEventArgs e)
        {
            var recordMessage = (RecordMesg)e.mesg;
            float s = recordMessage.GetSpeed() ?? 0f;
            if (s > 0f)
            {
                PointF from = new PointF(SemicirclesToDeg(recordMessage.GetPositionLat()), SemicirclesToDeg(recordMessage.GetPositionLong()));
                double dist = Distance(from, ActivityGhosts.initialGPSPoint);
                double bearing = -1 * Bearing(from, ActivityGhosts.initialGPSPoint);
                pointList.Add(new GeoPoint((float)(dist * Math.Cos(bearing)), (float)(dist * Math.Sin(bearing)), s));
            }
        }

        private void OnSessionMessage(object sender, MesgEventArgs e)
        {
            var sessionMessage = (SessionMesg)e.mesg;
            startTime = sessionMessage.GetStartTime().GetDateTime();
            sport = sessionMessage.GetSport() ?? Sport.Cycling;
        }

        private double Distance(PointF from, PointF to)
        {
            double dLat = (DegToRad(to.X) - DegToRad(from.X));
            double dLon = (DegToRad(to.Y) - DegToRad(from.Y));
            double latFrom = DegToRad(from.X);
            double latTo = DegToRad(to.X);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(latFrom) * Math.Cos(latTo) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)) * 6378137.0f;
        }

        private double Bearing(PointF from, PointF to)
        {
            double dLon = (DegToRad(to.Y) - DegToRad(from.Y));
            double latFrom = DegToRad(from.X);
            double latTo = DegToRad(to.X);
            double y = Math.Sin(dLon) * Math.Cos(latTo);
            double x = Math.Cos(latFrom) * Math.Sin(latTo) -
                       Math.Sin(latFrom) * Math.Cos(latTo) * Math.Cos(dLon);
            return Math.Atan2(y, x) + (Math.PI / 2);
        }

        private float SemicirclesToDeg(int? angleSemi)
        {
            return (float)(angleSemi * (180.0f / int.MaxValue));
        }

        private double DegToRad(float angleDeg)
        {
            return angleDeg * Math.PI / 180.0f;
        }
    }

    public class ActivityFile
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public float Lat { get; set; }
        public float Long { get; set; }
    }
}
