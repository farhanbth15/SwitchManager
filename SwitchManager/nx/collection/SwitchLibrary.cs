﻿using SwitchManager.nx.library;
using SwitchManager.nx.cdn;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using SwitchManager.util;

namespace SwitchManager.nx.library
{
    /// <summary>
    /// This is the primary class for the switch library and cdn downloader. It manages all existing title keys,
    /// library metadata, downloading, eshop access and anything else pretty much. It has XML attributes because it
    /// is much easier to serialize this class a a copycat of the LibraryMetadata class, but the LibraryMetadata class
    /// is the one to use for loading/deserializing data, which should then be copied into the appropriate collection items.
    /// 
    /// The library should be populated before use by loading from a title keys file or from library metadata, or both.
    /// </summary>
    [XmlRoot(ElementName = "Library")]
    public class SwitchLibrary
    {
        [XmlElement(ElementName = "CollectionItem")]
        public SwitchTitleCollection Collection { get; set; }

        [XmlIgnore]
        public CDNDownloader Loader { get; set; }

        [XmlIgnore]
        public string RomsPath { get; set; } = ".";

        [XmlIgnore]
        public bool RemoveContentAfterRepack { get; set; } = false;

        [XmlIgnore]
        public string ImagesPath { get; set; }

        private Dictionary<string, SwitchCollectionItem> titlesByID = new Dictionary<string, SwitchCollectionItem>();

        /// <summary>
        /// This default constructor is ONLY so that XmlSerializer will stop complaining. Don't use it, unless
        /// you remember to also set the loader, the image path and the rom path!
        /// </summary>
        public SwitchLibrary()
        {
            this.Collection = new SwitchTitleCollection();
        }

        public SwitchLibrary(CDNDownloader loader, string imagesPath, string romsPath) : this()
        {
            this.Loader = loader;
            this.ImagesPath = imagesPath;
            this.RomsPath = romsPath;
        }

        internal SwitchCollectionItem AddTitle(SwitchCollectionItem item)
        {
            if (item != null)
            {
                Collection.Add(item);
                titlesByID[item.Title.TitleID] = item;
            }
            return item;
        }

        internal SwitchCollectionItem AddGame(SwitchGame game)
        {
            if (game != null)
            {
                var item = new SwitchCollectionItem(game);
                return AddTitle(item);
            }
            return null;
        }

        internal SwitchCollectionItem NewGame(string name, string titleid, string titlekey, SwitchCollectionState state = SwitchCollectionState.NotOwned, bool isFavorite = false)
        {
            // Already there, probably because DLC was listed before a title
            if (titlesByID.ContainsKey(titleid))
            {
                SwitchCollectionItem item = titlesByID[titleid];
                item.Title.Name = name;
                item.Title.TitleKey = titlekey;
                item.State = state;
                item.IsFavorite = isFavorite;
                return item;
            }
            else
            {
                SwitchGame game = new SwitchGame(name, titleid, titlekey);
                SwitchCollectionItem item = new SwitchCollectionItem(game, state, isFavorite);
                return item;
            }
        }

        internal SwitchCollectionItem AddTitle(SwitchTitle title)
        {
            SwitchCollectionItem item = new SwitchCollectionItem(title);
            AddTitle(item);
            return item;
        }

        /// <summary>
        /// Loads library metadata. This data is related directly to your collection, rather than titles or keys and whatnot.
        /// </summary>
        /// <param name="filename"></param>
        internal async Task LoadMetadata(string path)
        {
            if (!path.EndsWith(".xml"))
                path += ".xml";
            path = Path.GetFullPath(path);

            XmlSerializer xml = new XmlSerializer(typeof(LibraryMetadata));
            LibraryMetadata metadata;
            // Create a new file stream to write the serialized object to a file

            if (!File.Exists(path))
            {
                Console.WriteLine("Library metadata XML file doesn't exist, one will be created when the app closes.");
                return;
            }

            using (FileStream fs = File.OpenRead(path))
                metadata = xml.Deserialize(fs) as LibraryMetadata;

            if (metadata?.Items != null)
            {
                var versions = await Loader.GetLatestVersions().ConfigureAwait(false);
                foreach (var item in metadata.Items)
                {
                    SwitchCollectionItem ci = GetTitleByID(item.TitleID);
                    if (ci == null)
                    {
                        ci = LoadTitle(item.TitleID, item.TitleKey, item.Name, versions);
                    }
                    else
                    {
                        if (item.Name != null) ci.Title.Name = item.Name;
                        if (item.TitleKey != null) ci.Title.TitleKey = item.TitleKey;
                    }
                    ci.IsFavorite = item.IsFavorite;
                    ci.RomPath = item.Path;
                    ci.State = item.State;
                    ci.Size = item.Size;

                    foreach (var update in item.Updates)
                    {
                        AddUpdateTitle(update.TitleID, item.TitleID, item.Name, update.Version, update.TitleKey);
                    }
                }

            }

            Console.WriteLine($"Finished loading library metadata from {path}");
        }
        
        internal void SaveMetadata(string path)
        {
            if (!path.EndsWith(".xml"))
                path += ".xml";
            path = Path.GetFullPath(path);

            XmlSerializer xml = new XmlSerializer(typeof(SwitchLibrary));

            // Create a new file stream to write the serialized object to a file
            FileStream fs = File.Exists(path) ? File.Open(path, FileMode.Truncate, FileAccess.Write) : File.Create(path);
            xml.Serialize(fs, this);
            fs.Dispose();

            // TODO: Save updates to metadata, even though updates are inside the base title instead of in the main collection
            Console.WriteLine($"Finished saving library metadata to {path}");
        }

        /// <summary>
        /// Scans a folder for existing roms and updates the collection.
        /// </summary>
        /// <param name="path"></param>
        internal void ScanRomsFolder(string path)
        {
            DirectoryInfo dinfo = new DirectoryInfo(path);
            if (!dinfo.Exists)
                throw new DirectoryNotFoundException($"Roms directory {path} not found.");

            foreach (var nspFile in dinfo.EnumerateFiles("*.nsp"))
            {
                string fname = nspFile.Name; // base name
                fname = Path.GetFileNameWithoutExtension(fname); // remove .nsp
                var fileParts = fname.Split();
                if (fileParts == null || fileParts.Length < 2)
                    continue;

                string meta = fileParts.Last();

                SwitchTitleType type = SwitchTitleType.Unknown;
                string name = null;
                string id = null;
                string version = null;

                // Lets parse the file name to get name, id and version
                // Also check for [DLC] and [UPD] signifiers
                // I could use a Regex but I'm not sure that would be faster or easier to do
                if ("[DLC]".Equals(fileParts[0].ToLower()))
                {
                    type = SwitchTitleType.DLC;
                    name = string.Join(" ", fileParts.Where((s, idx) => idx > 0 && idx < fileParts.Length - 1));
                }
                else
                {
                    name = string.Join(" ", fileParts.Where((s, idx) => idx < fileParts.Length - 1));
                    if (meta.StartsWith("[UPD]"))
                    {
                        type = SwitchTitleType.Update;
                        meta = meta.Remove(0, 5);
                    }
                    else
                    {
                        if (name.ToUpper().Contains("DEMO"))
                        {
                            type = SwitchTitleType.Demo;
                        }
                        else
                        {
                            type = SwitchTitleType.Game;
                        }
                    }
                }

                if (meta.StartsWith("[") && meta.EndsWith("]"))
                {
                    string[] metaParts = meta.Split(new string[] { "][" }, StringSplitOptions.RemoveEmptyEntries);
                    
                    if (metaParts.Length > 1)
                    {
                        string verPart = metaParts[1];
                        if (verPart.EndsWith("]"))
                            verPart = verPart.Remove(verPart.Length - 1);
                        if (verPart.StartsWith("v"))
                            verPart = verPart.Remove(0, 1);
                        version = verPart;
                    }
                    if (metaParts.Length > 0)
                    {
                        string idPart = metaParts[0];
                        if (idPart.StartsWith("["))
                            idPart = idPart.Remove(0, 1);
                        if (idPart.EndsWith("]"))
                            idPart = idPart.Remove(idPart.Length - 1);
                        id = idPart;
                    }
                }

                switch (type)
                {
                    case SwitchTitleType.DLC:
                    case SwitchTitleType.Game:
                    case SwitchTitleType.Demo:
                        var item = GetTitleByID(id);
                        if (item != null && id.Equals(item.TitleId))
                        {
                            item.RomPath = nspFile.FullName;

                            // If you haven't already marked the file as on switch, mark it owned
                            if (item.State != SwitchCollectionState.OnSwitch)
                                item.State = SwitchCollectionState.Owned;
                        }
                        break;
                    case SwitchTitleType.Update:
                        AddUpdateTitle(id, null, name, uint.Parse(version), null);

                        break;
                    default:
                        break;
                }
            }
        }

        private SwitchUpdate AddUpdateTitle(string updateid, string gameid, string name, uint version, string titlekey)
        {
            string id = gameid ?? (updateid == null ? null : SwitchTitle.GetBaseGameIDFromUpdate(updateid));
            if (id != null)
            {
                SwitchUpdate update = new SwitchUpdate(name, gameid, version, titlekey);
                return AddUpdateTitle(update);
            }
            return null;
        }

        private SwitchUpdate AddUpdateTitle(SwitchUpdate update)
        {
            var baseT = GetTitleByID(update.GameID); // Let's try adding this to the base game's list
            if (baseT == null)
            {
                Console.WriteLine("WARNING: Found an update for a game that doesn't exist.");
                return null;
            }
            else if (baseT.Title == null)
            {
                Console.WriteLine("WARNING: Found a collection item in the library with a null title.");
                return null;
            }
            else if (baseT.Title.IsGame)
            {
                SwitchGame game = baseT.Title as SwitchGame;
                if (game.Updates == null)
                    game.Updates = new ObservableCollection<SwitchUpdate>();

                game.Updates.Add(update);
            }
            return update;
        }

        /// <summary>
        /// Initiates a title download. 
        /// Note - you MUST match the version and the title id!
        /// If you try to download a game title with a version number greater than 0, it will fail!
        /// If you try to download an update title with a version number of 0, it will fail!
        /// I have no idea what will even happen if you try to download a DLC.
        /// </summary>
        /// <param name="titleItem"></param>
        /// <param name="v"></param>
        /// <param name="repack"></param>
        /// <param name="verify"></param>
        /// <returns></returns>
        public async Task<string> DownloadTitle(SwitchTitle title, uint v, bool repack, bool verify)
        {
            if (title == null)
                throw new Exception($"No title selected for download");

            string dir = this.RomsPath + Path.DirectorySeparatorChar + title.TitleID;
            DirectoryInfo dinfo = new DirectoryInfo(dir);
            if (!dinfo.Exists)
                dinfo.Create();
            
            try
            {
                // Download a base version with a game ID
                if (v == 0)
                {
                    if (SwitchTitle.IsBaseGameID(title.TitleID))
                    {
                        return await DoNspDownloadAndRepack(title, v, dinfo, repack, verify).ConfigureAwait(false);
                    }
                    else if (SwitchTitle.IsDLCID(title.TitleID))
                    {
                        return await DoNspDownloadAndRepack(title, v, dinfo, repack, verify).ConfigureAwait(false);
                    }
                    else
                        throw new Exception("Don't try to download an update with version 0!");
                }
                else
                {
                    if (SwitchTitle.IsBaseGameID(title.TitleID))
                        throw new Exception("Don't try to download an update using base game's ID!");
                    else if (SwitchTitle.IsDLCID(title.TitleID))
                        throw new Exception("Don't try to download an update using a DLC ID!");
                    else
                    {
                        return await DoNspDownloadAndRepack(title, v, dinfo, repack, verify).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                // TODO delete directory after
                //dinfo.Delete(true);
            }
        }

        private async Task<string> DoNspDownloadAndRepack(SwitchTitle title, uint version, DirectoryInfo dir, bool repack, bool verify)
        {
            var nsp = await Loader.DownloadTitle(title, version, dir.FullName, repack, verify).ConfigureAwait(false);

            if (repack)
            {
                string titleName = Miscellaneous.SanitizeFileName(title.Name);

                // format is
                // [DLC] at the start, plus space, if it is DLC - this is already party of the name for DLC, typically
                // title name
                // [UPD] if it is an update
                // [titleid]
                // [vXXXXXX], where XXXXXX is the version number in decimal
                string nspFile = (title.Type == SwitchTitleType.DLC && !titleName.StartsWith("[DLC]") ? "[DLC] " : "") + titleName + (title.Type == SwitchTitleType.Update?" [UPD]":" ") + $"[{title.TitleID}][v{version}].nsp";
                string nspPath = $"{this.RomsPath}{Path.DirectorySeparatorChar}{nspFile}";

                // Repack the game files into an NSP
                bool success = await nsp.Repack(nspPath).ConfigureAwait(false);

                // If the NSP failed somehow but the file exists any, remove it
                if (!success && File.Exists(nspPath))
                    File.Delete(nspPath);

                if (this.RemoveContentAfterRepack)
                    dir.Delete(true);

                return nspPath;
            }
            return dir.FullName;
        }

        /// <summary>
        /// Executes a download of a title and/or updates/DLC, according to the options presented.
        /// TODO: Test this
        /// TODO: DLC
        /// </summary>
        /// <param name="titleItem"></param>
        /// <param name="v"></param>
        /// <param name="options"></param>
        /// <param name="repack"></param>
        /// <param name="verify"></param>
        /// <returns></returns>
        internal async Task DownloadGame(SwitchCollectionItem titleItem, uint v, DownloadOptions options, bool repack, bool verify)
        {
            SwitchTitle title = titleItem?.Title;
            if (title == null)
                throw new Exception($"No title selected for download");

            switch (options)
            {
                case DownloadOptions.AllDLC:
                    if (SwitchTitle.IsDLCID(title.TitleID))
                    {
                        string dlcPath = await DownloadTitle(title, title.Versions.Last(), repack, verify);
                        titleItem.State = SwitchCollectionState.Owned;
                        titleItem.RomPath = Path.GetFullPath(dlcPath);
                    }
                    else if (title.IsGame)
                    {
                        SwitchGame game = title as SwitchGame;
                        if (game.DLC != null && game.DLC.Count > 0)
                        foreach (var t in game.DLC)
                        {
                            SwitchCollectionItem dlcTitle = GetTitleByID(t?.TitleID);
                            string dlcPath = await DownloadTitle(dlcTitle?.Title, title.Versions.Last(), repack, verify);
                            dlcTitle.State = SwitchCollectionState.Owned;
                            dlcTitle.RomPath = Path.GetFullPath(dlcPath);
                        }
                    }
                    break;
                case DownloadOptions.UpdateAndDLC:
                    goto case DownloadOptions.UpdateOnly;

                case DownloadOptions.BaseGameAndUpdateAndDLC:
                    goto case DownloadOptions.BaseGameOnly;

                case DownloadOptions.BaseGameAndUpdate:
                    goto case DownloadOptions.BaseGameOnly;
                    
                case DownloadOptions.UpdateOnly:
                    if (v == 0) return;

                    // If a version greater than 0 is selected, download it and every version below it
                    while (v > 0)
                    {
                        SwitchUpdate update = title.GetUpdateTitle(v);
                        string updatePath = await DownloadTitle(update, v, repack, verify);
                        AddUpdateTitle(update);
                        v -= 0x10000;
                    }

                    if (options == DownloadOptions.UpdateAndDLC || options == DownloadOptions.BaseGameAndUpdateAndDLC)
                        goto case DownloadOptions.AllDLC;
                    break;

                case DownloadOptions.BaseGameAndDLC:
                    goto case DownloadOptions.BaseGameOnly;

                case DownloadOptions.BaseGameOnly:
                default:
                    string romPath = await DownloadTitle(title, title.Versions.Last(), repack, verify);
                    titleItem.State = SwitchCollectionState.Owned;
                    titleItem.RomPath = Path.GetFullPath(romPath);

                    if (options == DownloadOptions.BaseGameAndUpdate || options == DownloadOptions.BaseGameAndUpdateAndDLC)
                        goto case DownloadOptions.UpdateOnly;
                    else if (options == DownloadOptions.BaseGameAndDLC)
                        goto case DownloadOptions.AllDLC;
                    break;
            }
        }

        /// <summary>
        /// Loads all title icons in the background. It does so asynchronously, so the caller better be able to update
        /// the image file display at any time after the calls complete. If preload is true, it also tries to remotely load
        /// every single image if it isn't found locally.
        /// </summary>
        /// <param name="localPath"></param>
        /// <param name="preload">Set preload to true to load all images at once if they aren't available locally. False will return a blank image if it isn't found in the cache.</param>
        internal void LoadTitleIcons(string localPath, bool preload = false)
        {
            foreach (SwitchCollectionItem item in Collection)
            {
                Task.Run(()=>LoadTitleIcon(item.Title, preload)); // This is async, let it do its thing we don't need the results now
            }
        }

        /// <summary>
        /// Gets a title icon. If it isn't cached locally, gets it from nintendo. Only loads a local image if downloadRemote is false, but will download
        /// from the CDN if downloadRemote is true.
        /// </summary>
        /// <param name="title">Title whose icon you wish to load</param>
        /// <param name="downloadRemote">If true, loads the image from nintendo if it isn't found in cache</param>
        /// <returns></returns>
        public async Task LoadTitleIcon(SwitchTitle title, bool downloadRemote = false)
        {
            SwitchImage img = GetLocalImage(title.TitleID);
            if (img == null && downloadRemote && SwitchTitle.IsBaseGameID(title.TitleID))
            {
                // Ask the image loader to get the image remotely and cache it
                await Loader.DownloadRemoteImage(title);
                img = GetLocalImage(title.TitleID);
            }
            // Return cached image, or blank if it couldn't be found

            if (img == null)
                title.Icon = BlankImage;
            else
                title.Icon = img;
        }

        public SwitchImage GetLocalImage(string titleID)
        {
            string path = Path.GetFullPath(this.ImagesPath);
            if (Directory.Exists(path))
            {
                string location = path + Path.DirectorySeparatorChar + titleID + ".jpg";
                if (File.Exists(location))
                {
                    SwitchImage img = new SwitchImage(location);
                    return img;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                Directory.CreateDirectory(this.ImagesPath);
            }

            return null;
        }
        
        public static SwitchImage BlankImage { get { return new SwitchImage("Images\\blank.jpg"); } }


        public async Task LoadTitleKeysFile(string filename)
        {
            var lines = File.ReadLines(filename);
            var versions = await Loader.GetLatestVersions().ConfigureAwait(false);

            foreach (var line in lines)
            {
                string[] split = line.Split('|');
                string tid = split[0]?.Trim()?.Substring(0, 16);
                string tkey = split[1]?.Trim()?.Substring(0, 32);
                string name = split[2]?.Trim();

                LoadTitle(tid, tkey, name, versions);
            }
        }

        public async Task<ICollection<SwitchCollectionItem>> UpdateTitleKeysFile(string file)
        {
            var lines = File.ReadLines(file);
            var versions = await Loader.GetLatestVersions().ConfigureAwait(false);

            var newTitles = new List<SwitchCollectionItem>();
            foreach (var line in lines)
            {
                string[] split = line.Split('|');
                string tid = split[0]?.Trim()?.Substring(0, 16);
                SwitchCollectionItem item = GetTitleByID(tid);
                if (item == null)
                {
                    // New title!!
                    string tkey = split[1]?.Trim()?.Substring(0, 32);
                    string name = split[2]?.Trim();
                    item = LoadTitle(tid, tkey, name, versions);
                    item.State = SwitchCollectionState.New;
                    newTitles.Add(item);
                }
            }

            return newTitles;
        }

        /// <summary>
        /// Loads a title into the collection. Whether the collection is being populated by a titlekeys file or by
        /// the library metadata, you'll want to call this for each item to make sure all the right logic gets executed
        /// for adding a game (or DLC, or update) to the library.
        /// </summary>
        /// <param name="tid">16-digit hex Title ID</param>
        /// <param name="tkey">32-digit hex Title Key</param>
        /// <param name="name">The name of game or DLC</param>
        /// <param name="versions">The dictionary of all the latest versions for every game. Get this via the CDN.</param>
        /// <returns></returns>
        private SwitchCollectionItem LoadTitle(string tid, string tkey, string name, Dictionary<string,uint> versions)
        {
            if (SwitchTitle.IsBaseGameID(tid))
            {
                var item = NewGame(name, tid, tkey);
                var game = item?.Title as SwitchGame;
                if (versions.ContainsKey(game.TitleID))
                {
                    uint v = versions[game.TitleID];
                    game.LatestVersion = v;
                }

                AddTitle(item);
                return item;
            }
            else if (name.StartsWith("[DLC]") || SwitchTitle.IsDLCID(tid))
            {
                // basetid = '%s%s000' % (tid[:-4], str(int(tid[-4], 16) - 1))
                string baseGameID = SwitchTitle.GetBaseGameIDFromDLC(tid);

                var dlc = new SwitchDLC(name, tid, baseGameID, tkey);
                try
                {
                    return AddDLCTitle(baseGameID, dlc);
                }
                catch (Exception)
                {
                    if (GetBaseTitleByID(baseGameID) == null)
                        Console.WriteLine($"WARNING: Couldn't find base game ID {baseGameID} for DLC {dlc.Name}");
                }
            }
            else
            {
                // ?? huh ??
            }
            return null;
        }

        /// <summary>
        /// Adds a DLC title (by ID) to the list of DLC of a base title (also looked up by ID)
        /// </summary>
        /// <param name="baseGameID">Title ID of base game.</param>
        /// <param name="dlcID">Title ID of base game's DLC, to add to the game's DLC list.</param>
        /// <returns>The base title that the DLC was attached to.</returns>
        public SwitchCollectionItem AddDLCTitle(string baseGameID, SwitchDLC dlc)
        {
            SwitchGame baseGame = GetBaseTitleByID(baseGameID)?.Title as SwitchGame;
            if (baseGame == null)
            {
                // This can happen if you put the DLC before the title, or if your titlekeys file has DLC for
                // titles that aren't in it. The one I'm using, for example, has fire emblem warriors JP dlc,
                // but not the game
                // If the game ends up being added later, AddGame is able to slide in the proper info over the stub we add here
                string name = dlc.Name.Replace("[DLC] ", "");
                baseGame = new SwitchGame(name, baseGameID, null);
                AddTitle(baseGame);
            }

            if (baseGame.IsGame)
            {
                SwitchGame game = baseGame as SwitchGame;
                if (game.DLC == null) game.DLC = new ObservableCollection<SwitchDLC>();

                game.DLC.Add(dlc);
            }
            return AddTitle(dlc);
        }

        public SwitchCollectionItem GetTitleByID(string titleID)
        {
            if (titleID == null || titleID.Length != 16)
                return null;

            return titlesByID.TryGetValue(titleID, out SwitchCollectionItem returnValue) ? returnValue : null;
        }

        public SwitchCollectionItem GetBaseTitleByID(string titleID)
        {
            if (titleID == null || titleID.Length != 16)
                return null;

            // In case someone tries to look up by UPDATE TID, convert to base game TID
            if (SwitchTitle.IsUpdateTitleID(titleID))
                titleID = SwitchTitle.GetBaseGameIDFromUpdate(titleID);
            else if (SwitchTitle.IsDLCID(titleID))
                titleID = SwitchTitle.GetBaseGameIDFromDLC(titleID);

            return GetTitleByID(titleID);
        }

        public SwitchGame GetBaseGameByID(string baseGameID)
        {
            return GetBaseTitleByID(baseGameID)?.Title as SwitchGame;
        }
    }
}
