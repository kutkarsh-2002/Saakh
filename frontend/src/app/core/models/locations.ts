/**
 * Location options for the signup and profile forms.
 *
 * Grounded in real Indian states and districts, matching the taxonomy the seed
 * data uses, so a reviewer never sees a placeholder region that does not exist.
 * The list covers the states the pilot data spans rather than all 36 states and
 * union territories; a fuller gazetteer would come from a reference table.
 */
export interface StateOption {
  name: string;
  districts: string[];
}

export const INDIAN_STATES: StateOption[] = [
  {
    name: 'Andhra Pradesh',
    districts: ['Visakhapatnam', 'Guntur', 'Krishna', 'Kurnool'],
  },
  {
    name: 'Bihar',
    districts: ['Patna', 'Gaya', 'Muzaffarpur', 'Bhagalpur'],
  },
  {
    name: 'Delhi',
    districts: ['Central Delhi', 'North Delhi', 'South Delhi', 'West Delhi'],
  },
  {
    name: 'Gujarat',
    districts: ['Ahmedabad', 'Surat', 'Rajkot', 'Vadodara', 'Bhavnagar'],
  },
  {
    name: 'Haryana',
    districts: ['Gurugram', 'Faridabad', 'Hisar', 'Karnal'],
  },
  {
    name: 'Karnataka',
    districts: ['Bengaluru Urban', 'Mysuru', 'Belagavi', 'Hubballi-Dharwad', 'Mangaluru'],
  },
  {
    name: 'Kerala',
    districts: ['Ernakulam', 'Thiruvananthapuram', 'Kozhikode', 'Thrissur'],
  },
  {
    name: 'Madhya Pradesh',
    districts: ['Indore', 'Bhopal', 'Jabalpur', 'Gwalior'],
  },
  {
    name: 'Maharashtra',
    districts: ['Pune', 'Nashik', 'Mumbai Suburban', 'Nagpur', 'Thane', 'Aurangabad'],
  },
  {
    name: 'Odisha',
    districts: ['Khordha', 'Cuttack', 'Sambalpur', 'Ganjam'],
  },
  {
    name: 'Punjab',
    districts: ['Ludhiana', 'Amritsar', 'Jalandhar', 'Patiala'],
  },
  {
    name: 'Rajasthan',
    districts: ['Jaipur', 'Jodhpur', 'Kota', 'Udaipur', 'Ajmer'],
  },
  {
    name: 'Tamil Nadu',
    districts: ['Chennai', 'Coimbatore', 'Madurai', 'Tiruchirappalli', 'Salem'],
  },
  {
    name: 'Telangana',
    districts: ['Hyderabad', 'Warangal', 'Karimnagar', 'Nizamabad'],
  },
  {
    name: 'Uttar Pradesh',
    districts: ['Kanpur Nagar', 'Lucknow', 'Varanasi', 'Agra', 'Meerut', 'Prayagraj'],
  },
  {
    name: 'West Bengal',
    districts: ['Kolkata', 'Howrah', 'Siliguri', 'Darjeeling', 'Nadia'],
  },
];
