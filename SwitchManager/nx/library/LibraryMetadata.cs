﻿using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace SwitchManager.nx.library
{
    [XmlRoot(ElementName = "Library")]
    public class LibraryMetadata
    {
        [XmlElement(ElementName = "CollectionItem")]
        public LibraryMetadataItem[] Items { get; set; }
    }

    [XmlRoot(ElementName = "CollectionItem")]
    public class LibraryMetadataItem
    {
        [XmlElement(ElementName = "Title")]
        public string TitleID { get; set; }

        [XmlElement(ElementName = "Key")]
        public string TitleKey { get; set; }

        [XmlElement(ElementName = "Icon")]
        public string Icon { get; set; }

        [XmlElement(ElementName = "Name")]
        public string Name { get; set; }

        [XmlElement(ElementName = "Version")]
        public uint? Version { get; set; }

        [XmlElement(ElementName = "LatestVersion")]
        public uint? LatestVersion { get; set; }

        [XmlElement(ElementName = "Intro")]
        public string Intro { get; set; }

        [XmlElement(ElementName = "Publisher")]
        public string Publisher { get; set; }

        [XmlElement(ElementName = "Developer")]
        public string Developer { get; set; }

        [XmlElement(ElementName = "Category")]
        public string Category { get; set; }

        [XmlElement(ElementName = "BoxArt")]
        public string BoxArtUrl { get; set; }

        [XmlElement(ElementName = "OfficialSite")]
        public string OfficialSite { get; set; }

        [XmlElement(ElementName = "ReleaseDate")]
        public DateTime? ReleaseDate { get; set; }

        [XmlElement(ElementName = "Rating")]
        public string Rating { get; set; }

        [XmlElement(ElementName = "RatingContent")]
        public string RatingContent { get; set; }

        [XmlElement(ElementName = "NumPlayers")]
        public uint? NumPlayers { get; set; }

        [XmlElement(ElementName = "Size")]
        public long? Size { get; set; }

        [XmlElement(ElementName = "Added")]
        public DateTime? Added { get; set; }

        [XmlElement(ElementName = "NsuId")]
        public string NsuId { get; set; }

        [XmlElement(ElementName = "ProductId")]
        public string ProductId { get; set; }

        [XmlElement(ElementName = "SLUG")]
        public string SLUG { get; set; }

        [XmlElement(ElementName = "ProductCode")]
        public string ProductCode { get; set; }

        [XmlElement(ElementName = "HasDLC")]
        public bool? HasDLC { get; set; }

        [XmlElement(ElementName = "HasAmiibo")]
        public bool? HasAmiibo { get; set; }

        [XmlElement(ElementName = "Price")]
        public string Price { get; set; }

        [XmlElement(ElementName = "Description")]
        public string Description { get; set; }

        [XmlElement(ElementName = "State")]
        public SwitchCollectionState? State { get; set; }

        [XmlElement(ElementName = "Favorite")]
        public bool? IsFavorite { get; set; }

        [XmlElement(ElementName = "Path")]
        public string Path { get; set; }

        [XmlElement(ElementName = "DisplayVersion")]
        public string DisplayVersion { get; set; }

        [XmlElement(ElementName = "Region")]
        public string Region { get; set; }

        [XmlElement(ElementName = "IsDemo")]
        public bool? IsDemo { get; set; }

        [XmlElement(ElementName = "RequiredSystemVersion")]
        public long? RequiredSystemVersion { get; set; }

        [XmlElement(ElementName = "MasterKeyRevision")]
        public byte? MasterKeyRevision { get; set; }

        [XmlElement(ElementName = "Update")]
        public List<LibraryMetadataItem> Updates { get; set; }
    }
}