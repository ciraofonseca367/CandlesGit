# Loop over files and get features
import os, glob, os.path, shutil
import numpy as np
from sklearn.cluster import KMeans
import json

imgdir = "/Users/cironola/CandlesFaceImages/Run_ToCluster/BTCUSDT"
targetdir = "/Users/cironola/CandlesFaceImages/ClusteredImages/BTCUSDT"
#number_clusters = 10

filelist = glob.glob(os.path.join(imgdir, '*.gif'))
filelist.sort()
featurelist = []
lenFeatures = []
for i, imagepath in enumerate(filelist):
    print("    Status: %s / %s" %(i, len(filelist)), end="\r")

    features = []
    f = open(imagepath.replace("gif","meta"))
    try:        
        data = json.load(f)
        for key, value in data.items():            
            features.append(int(key))
            for item in value:
                features.append(item)

        lenFeatures.append(len(features))
        featurelist.append(features)

    finally:
        f.close()

print
    

# Clustering
kmeans = KMeans(n_clusters=number_clusters, random_state=0).fit(np.array(featurelist))

# Copy images renamed by cluster 
# Check if target dir exists
try:
    os.makedirs(targetdir)
except OSError:
    pass

print("\n")
for i, m in enumerate(kmeans.labels_):
    print("    Copy: %s / %s" %(i+1, len(kmeans.labels_)), end="\r")

    groupDir = f"{targetdir}/{str(m)}/"

    try:
        os.makedirs(groupDir)
    except OSError:
        pass

    filePath = filelist[i]
    shutil.copy(filePath, f"{groupDir}/{os.path.basename(filePath)}.gif")
