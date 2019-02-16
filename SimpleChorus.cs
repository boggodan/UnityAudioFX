using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//This is a very, very simple chorus/delay/flanger/fx. 
//It's designed to help understand how to write a simple effect with OnAudioFilterRead.
//It's MONO - it can be easily upgraded to stereo, but I kept it simple to minimise plumbing/cruft.
//example audio is from:
//https://freesound.org/people/Paul%20Evans/sounds/256999/
public class SimpleChorus : MonoBehaviour {

    [Range(0.0f, 1.0f)] 
    public float delay_secs = 0.016f; //from 0.0 to 1.0f in seconds. You want a TINY value here for chorus.
    [Range(0.0f, 1.0f)]
    public float feedback = 0.0f; //from 0.0 to 1.0
    [Range(0.0f, 1.0f)]
    public float wetmix = 0.8f; //from 0.0 to 1.0
    [Range(0.0f,1.0f)]
    public float amount = 0.6f; //from 0.0 to 1.0
    [Range(0.0f, 40.0f)]
    public float rate = 0.5f; //in hz
    
    private int max_delay; //we compute this from the sample rate
    private float[] audio_buffer;
    private int write_head = 0;
    private float sampleRate = 0.0f;
    private float sine_phase = 0.0f; //lfo sine wave phase
    private bool ready = false;
    
    // Use this for initialization
    void Start () {
        // we want up to a second of audio memory allocated - this is equal to the sample rate, but you can make it higher
        max_delay = AudioSettings.outputSampleRate;
        //we just need to store the sample rate somewhere.
        sampleRate = AudioSettings.outputSampleRate;
        //create an empty buffer for the delay effect - it has a second's worth of audio, based on the sample rate
        audio_buffer = new float[max_delay];
        ready = true; //this means the audio thread can now do stuff
    }
    
    // Update is called once per frame
    void Update () {
    	//nothing to do here, it's an audio effect, so all the code is in OnAudioFilterRead
    }
    
    // Useful function to wrap the read/write positions around into the buffer.
    float wrap(float a, float b)
    {
        return (a % b + b) % b;
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        //this function can be called before Start(), so we need to make sure audio_buffer is initialised
        if (!ready)
            return;
    
        //audio samples in the "data" buffer are interleaved channel by channel
        //because the chorus effect is mono, it's useful to think of it in terms of "mono" samples
        //for the bulk of algorithm, so we can divide by the number of channels here.
        int dataLen = data.Length / channels;
    
        //this is how many samples our delay (expressed in seconds) actually is
        float delay_samples = delay_secs * sampleRate;
    
        //go through the input/output data
        for(int n = 0; n<dataLen; n++)
        {
            float lfo = (Mathf.Sin(sine_phase * 2.0f * Mathf.PI) * 0.5f + 1.0f); //this is a sine wave that goes from 0.0 to 1.0
            lfo = lfo * delay_samples * amount; //now the sine wave is going from 0.0 to the delay time * the amount of wobble we want
            //this means we can be reading as far back as a second, and moving the read head all the way up to the write head, when amount is max
            float read_pos = ((float)write_head - delay_samples) + lfo; //our read position - write head - delay, and then we oscillate that forward by the LFO
    
            //now for the linear interpolation - we need the sample index before and after the fractional read head position
            float read_pos_1 = Mathf.Floor(read_pos);
            float read_pos_2 = Mathf.Ceil(read_pos);
            float lerp_t = read_pos - read_pos_1; //and this is how far between the lower and upper sample indices we are trying to read
    
            //because we are oscillating the read head, it can easily be pushed outside the buffer's bounds
            //but we can wrap it into the buffer's bounds with a tiny function.
            //because the buffer we've implemented is a "ring buffer", we are storing a history of audio data
            //which wraps around the buffer - for example, we could be reading at writing at sample 100 and reading
            //500 samples into the past, which is closer to the end of the buffer at 44000! That's fine!
            read_pos_1 = wrap(read_pos_1, max_delay);
            read_pos_2 = wrap(read_pos_2, max_delay);
    
            //now we do a linearly interpolated read from the audio buffer - we interpolate between signal values
            //at the higher and lower positions - these may have wrapped around at the boundaries of the buffer, but remember
            //that's fine.
            float read_from_buffer = Mathf.Lerp(audio_buffer[(int)read_pos_1], audio_buffer[(int)read_pos_2], lerp_t); //audio_buffer[(int)read_pos];
    
            //this is what we will write into the buffer - we compute this from the incoming audio
            float mix_into_buffer = 0.0f;
    
            //Now iterate over the audio channels.
            for (int c = 0; c < channels; c++)
            {
                //this is what we will write into the buffer - it's just the input merged together from all the channels
                mix_into_buffer += data[n * channels + c];
    
                //now write the output audio (per channnel). It's the input audio + what we READ from the buffer at the delayed read head
                data[n * channels + c] += read_from_buffer*wetmix; //here we can apply the wet mix, i.e. how much of the delayed signal do we want
            }
    
            //because we mixed together a signal from multiple channels into one buffer, we should probably
            //divide by the number of channels here or it will be super loud.
            //ideally though you should have one delay buffer PER channel - this is just a toy example!
            mix_into_buffer /= (float)channels;
    
            //now to write into the buffer!
            //we are going to write the audio merged together from all channels, which we collected earlier
            //but - we also add-ing the signal we read from the buffer x feedback - this is the feedback path!
            audio_buffer[write_head] = mix_into_buffer + read_from_buffer*feedback;
            //increment write head position. This just goes forward one sample each time,
            //wrapping around at the end of the buffer.
            write_head = (write_head + 1) % max_delay; 
            //update the phase for the sine wave that we use for the LFO.
            sine_phase += rate/sampleRate;
        }
    }
}
