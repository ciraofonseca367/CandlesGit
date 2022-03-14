import cherrypy
import base64
import numpy as np
import matplotlib.pyplot as plt
import matplotlib.image as mpimg
from tensorflow import keras
import io

reloaded_model = keras.models.load_model("saved_model")
categories = ["BottomReserval", "HVS", "JacClose", "JacOpen", "Jac", "KickBack"]

def To_Features(image_base64, identifier, img_size=250):
    bytes = base64.b64decode(image_base64)

    fp = io.BytesIO(bytes)

    img_array = mpimg.imread(fp, format='gif')[:,:,:3]
    new_img = img_array

    return new_img

def OneHotToCat(classification):
    max_score = max(classification)
    index = np.where(classification==max_score)
    return categories[index[0][0]]

def GetPrediction(img_feat):
    np_img = np.array([img_feat])
    prediction = reloaded_model.predict(np_img)

    print(prediction.round(2))

    translated = OneHotToCat(prediction[0])
    return translated

class CandlePredictor (object):
    @cherrypy.expose
    def index(self):
        return "Index esta OK!"

    @cherrypy.expose
    def predict(self, identifier):
        request = cherrypy.request.body.read()

        str_prediction = GetPrediction(To_Features(request, identifier))
        print(str_prediction)
        return str_prediction

cherrypy.config.update({
    'server.socket_host': '0.0.0.0',
    'server.socket_port': 3389,
    'log.access_file': './access.log',
    'log.error_file': './error.log'
})

if __name__ == "__main__":
    cherrypy.quickstart(CandlePredictor())