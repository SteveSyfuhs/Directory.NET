package main

import (
	"context"
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"net/http"

	"github.com/hashicorp/terraform-plugin-sdk/v2/diag"
	"github.com/hashicorp/terraform-plugin-sdk/v2/helper/schema"
)

// Provider returns the Terraform provider for Directory.NET.
func Provider() *schema.Provider {
	return &schema.Provider{
		Schema: map[string]*schema.Schema{
			"endpoint": {
				Type:        schema.TypeString,
				Required:    true,
				DefaultFunc: schema.EnvDefaultFunc("DIRECTORYNET_ENDPOINT", nil),
				Description: "The URL of the Directory.NET REST API (e.g., https://dc1.example.com).",
			},
			"api_key": {
				Type:        schema.TypeString,
				Optional:    true,
				Sensitive:   true,
				DefaultFunc: schema.EnvDefaultFunc("DIRECTORYNET_API_KEY", nil),
				Description: "API key for authenticating with the Directory.NET REST API.",
			},
		},
		ResourcesMap: map[string]*schema.Resource{
			"directorynet_user":       resourceUser(),
			"directorynet_group":      resourceGroup(),
			"directorynet_ou":         resourceOU(),
			"directorynet_computer":   resourceComputer(),
			"directorynet_gpo":        resourceGPO(),
			"directorynet_dns_record": resourceDnsRecord(),
		},
		ConfigureContextFunc: providerConfigure,
	}
}

// apiClient holds the configured HTTP client and endpoint.
type apiClient struct {
	endpoint   string
	apiKey     string
	httpClient *http.Client
}

func providerConfigure(_ context.Context, d *schema.ResourceData) (interface{}, diag.Diagnostics) {
	endpoint := d.Get("endpoint").(string)
	apiKey := d.Get("api_key").(string)

	return &apiClient{
		endpoint:   endpoint,
		apiKey:     apiKey,
		httpClient: &http.Client{},
	}, nil
}

// apiRequest is a helper that performs HTTP requests against the Directory.NET REST API.
func (c *apiClient) apiRequest(ctx context.Context, method, path string, body interface{}) ([]byte, error) {
	url := fmt.Sprintf("%s/api/v1%s", c.endpoint, path)

	var reqBody io.Reader
	if body != nil {
		jsonBody, err := json.Marshal(body)
		if err != nil {
			return nil, fmt.Errorf("failed to marshal request body: %w", err)
		}
		reqBody = bytes.NewReader(jsonBody)
	}

	req, err := http.NewRequestWithContext(ctx, method, url, reqBody)
	if err != nil {
		return nil, fmt.Errorf("failed to create request: %w", err)
	}

	req.Header.Set("Content-Type", "application/json")
	if c.apiKey != "" {
		req.Header.Set("Authorization", "Bearer "+c.apiKey)
	}

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("request failed: %w", err)
	}
	defer resp.Body.Close()

	respBody, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, fmt.Errorf("failed to read response: %w", err)
	}

	if resp.StatusCode >= 400 {
		return nil, fmt.Errorf("API error %d: %s", resp.StatusCode, string(respBody))
	}

	return respBody, nil
}

// ---------------------------------------------------------------------------
// Resource: directorynet_user
// ---------------------------------------------------------------------------

func resourceUser() *schema.Resource {
	return &schema.Resource{
		CreateContext: resourceUserCreate,
		ReadContext:   resourceUserRead,
		UpdateContext: resourceUserUpdate,
		DeleteContext: resourceUserDelete,
		Schema: map[string]*schema.Schema{
			"sam_account_name": {Type: schema.TypeString, Required: true, Description: "The sAMAccountName for the user."},
			"display_name":     {Type: schema.TypeString, Optional: true, Description: "Display name of the user."},
			"given_name":       {Type: schema.TypeString, Optional: true},
			"surname":          {Type: schema.TypeString, Optional: true},
			"email":            {Type: schema.TypeString, Optional: true},
			"upn":              {Type: schema.TypeString, Optional: true, Description: "User principal name."},
			"ou":               {Type: schema.TypeString, Required: true, Description: "Distinguished name of the parent OU."},
			"enabled":          {Type: schema.TypeBool, Optional: true, Default: true},
			"password":         {Type: schema.TypeString, Optional: true, Sensitive: true},
			"dn":               {Type: schema.TypeString, Computed: true, Description: "The distinguished name of the created user."},
		},
	}
}

func resourceUserCreate(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	client := m.(*apiClient)
	body := map[string]interface{}{
		"samAccountName": d.Get("sam_account_name"),
		"displayName":    d.Get("display_name"),
		"givenName":      d.Get("given_name"),
		"sn":             d.Get("surname"),
		"mail":           d.Get("email"),
		"upn":            d.Get("upn"),
		"ou":             d.Get("ou"),
		"enabled":        d.Get("enabled"),
		"password":       d.Get("password"),
	}

	resp, err := client.apiRequest(ctx, http.MethodPost, "/users", body)
	if err != nil {
		return diag.FromErr(err)
	}

	var result map[string]interface{}
	if err := json.Unmarshal(resp, &result); err != nil {
		return diag.FromErr(err)
	}

	if dn, ok := result["distinguishedName"].(string); ok {
		d.SetId(dn)
		d.Set("dn", dn)
	}

	return nil
}

func resourceUserRead(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	// Stub: read user by DN from the API
	return nil
}

func resourceUserUpdate(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	// Stub: update user attributes via the API
	return nil
}

func resourceUserDelete(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	client := m.(*apiClient)
	dn := d.Id()
	_, err := client.apiRequest(ctx, http.MethodDelete, fmt.Sprintf("/objects/%s", dn), nil)
	if err != nil {
		return diag.FromErr(err)
	}
	d.SetId("")
	return nil
}

// ---------------------------------------------------------------------------
// Resource: directorynet_group
// ---------------------------------------------------------------------------

func resourceGroup() *schema.Resource {
	return &schema.Resource{
		CreateContext: resourceGroupCreate,
		ReadContext:   resourceGroupRead,
		UpdateContext: resourceGroupUpdate,
		DeleteContext: resourceGroupDelete,
		Schema: map[string]*schema.Schema{
			"sam_account_name": {Type: schema.TypeString, Required: true},
			"display_name":     {Type: schema.TypeString, Optional: true},
			"description":      {Type: schema.TypeString, Optional: true},
			"ou":               {Type: schema.TypeString, Required: true},
			"group_scope":      {Type: schema.TypeString, Optional: true, Default: "Global", Description: "DomainLocal, Global, or Universal."},
			"group_category":   {Type: schema.TypeString, Optional: true, Default: "Security", Description: "Security or Distribution."},
			"members":          {Type: schema.TypeSet, Optional: true, Elem: &schema.Schema{Type: schema.TypeString}},
			"dn":               {Type: schema.TypeString, Computed: true},
		},
	}
}

func resourceGroupCreate(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	client := m.(*apiClient)
	body := map[string]interface{}{
		"samAccountName": d.Get("sam_account_name"),
		"displayName":    d.Get("display_name"),
		"description":    d.Get("description"),
		"ou":             d.Get("ou"),
		"groupScope":     d.Get("group_scope"),
		"groupCategory":  d.Get("group_category"),
	}

	resp, err := client.apiRequest(ctx, http.MethodPost, "/groups", body)
	if err != nil {
		return diag.FromErr(err)
	}

	var result map[string]interface{}
	if err := json.Unmarshal(resp, &result); err != nil {
		return diag.FromErr(err)
	}

	if dn, ok := result["distinguishedName"].(string); ok {
		d.SetId(dn)
		d.Set("dn", dn)
	}
	return nil
}

func resourceGroupRead(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	return nil
}

func resourceGroupUpdate(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	return nil
}

func resourceGroupDelete(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	client := m.(*apiClient)
	_, err := client.apiRequest(ctx, http.MethodDelete, fmt.Sprintf("/objects/%s", d.Id()), nil)
	if err != nil {
		return diag.FromErr(err)
	}
	d.SetId("")
	return nil
}

// ---------------------------------------------------------------------------
// Resource: directorynet_ou
// ---------------------------------------------------------------------------

func resourceOU() *schema.Resource {
	return &schema.Resource{
		CreateContext: resourceOUCreate,
		ReadContext:   resourceOURead,
		UpdateContext: resourceOUUpdate,
		DeleteContext: resourceOUDelete,
		Schema: map[string]*schema.Schema{
			"name":             {Type: schema.TypeString, Required: true},
			"parent_dn":        {Type: schema.TypeString, Required: true, Description: "DN of the parent container."},
			"description":      {Type: schema.TypeString, Optional: true},
			"protect_deletion": {Type: schema.TypeBool, Optional: true, Default: true},
			"dn":               {Type: schema.TypeString, Computed: true},
		},
	}
}

func resourceOUCreate(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	client := m.(*apiClient)
	body := map[string]interface{}{
		"name":            d.Get("name"),
		"parentDn":        d.Get("parent_dn"),
		"description":     d.Get("description"),
		"protectDeletion": d.Get("protect_deletion"),
	}

	resp, err := client.apiRequest(ctx, http.MethodPost, "/ous", body)
	if err != nil {
		return diag.FromErr(err)
	}

	var result map[string]interface{}
	if err := json.Unmarshal(resp, &result); err != nil {
		return diag.FromErr(err)
	}

	if dn, ok := result["distinguishedName"].(string); ok {
		d.SetId(dn)
		d.Set("dn", dn)
	}
	return nil
}

func resourceOURead(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	return nil
}

func resourceOUUpdate(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	return nil
}

func resourceOUDelete(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	client := m.(*apiClient)
	_, err := client.apiRequest(ctx, http.MethodDelete, fmt.Sprintf("/objects/%s", d.Id()), nil)
	if err != nil {
		return diag.FromErr(err)
	}
	d.SetId("")
	return nil
}

// ---------------------------------------------------------------------------
// Resource: directorynet_computer
// ---------------------------------------------------------------------------

func resourceComputer() *schema.Resource {
	return &schema.Resource{
		CreateContext: resourceComputerCreate,
		ReadContext:   resourceComputerRead,
		UpdateContext: resourceComputerUpdate,
		DeleteContext: resourceComputerDelete,
		Schema: map[string]*schema.Schema{
			"sam_account_name": {Type: schema.TypeString, Required: true},
			"description":      {Type: schema.TypeString, Optional: true},
			"ou":               {Type: schema.TypeString, Required: true},
			"enabled":          {Type: schema.TypeBool, Optional: true, Default: true},
			"dn":               {Type: schema.TypeString, Computed: true},
		},
	}
}

func resourceComputerCreate(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	client := m.(*apiClient)
	body := map[string]interface{}{
		"samAccountName": d.Get("sam_account_name"),
		"description":    d.Get("description"),
		"ou":             d.Get("ou"),
		"enabled":        d.Get("enabled"),
	}

	resp, err := client.apiRequest(ctx, http.MethodPost, "/computers", body)
	if err != nil {
		return diag.FromErr(err)
	}

	var result map[string]interface{}
	if err := json.Unmarshal(resp, &result); err != nil {
		return diag.FromErr(err)
	}

	if dn, ok := result["distinguishedName"].(string); ok {
		d.SetId(dn)
		d.Set("dn", dn)
	}
	return nil
}

func resourceComputerRead(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	return nil
}

func resourceComputerUpdate(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	return nil
}

func resourceComputerDelete(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	client := m.(*apiClient)
	_, err := client.apiRequest(ctx, http.MethodDelete, fmt.Sprintf("/objects/%s", d.Id()), nil)
	if err != nil {
		return diag.FromErr(err)
	}
	d.SetId("")
	return nil
}

// ---------------------------------------------------------------------------
// Resource: directorynet_gpo
// ---------------------------------------------------------------------------

func resourceGPO() *schema.Resource {
	return &schema.Resource{
		CreateContext: resourceGPOCreate,
		ReadContext:   resourceGPORead,
		UpdateContext: resourceGPOUpdate,
		DeleteContext: resourceGPODelete,
		Schema: map[string]*schema.Schema{
			"display_name": {Type: schema.TypeString, Required: true},
			"description":  {Type: schema.TypeString, Optional: true},
			"status":       {Type: schema.TypeString, Optional: true, Default: "AllSettingsEnabled"},
			"links":        {Type: schema.TypeSet, Optional: true, Elem: &schema.Schema{Type: schema.TypeString}, Description: "DNs of OUs/domains to link this GPO to."},
			"gpo_id":       {Type: schema.TypeString, Computed: true},
			"dn":           {Type: schema.TypeString, Computed: true},
		},
	}
}

func resourceGPOCreate(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	client := m.(*apiClient)
	body := map[string]interface{}{
		"displayName": d.Get("display_name"),
		"description": d.Get("description"),
		"status":      d.Get("status"),
	}

	resp, err := client.apiRequest(ctx, http.MethodPost, "/gpos", body)
	if err != nil {
		return diag.FromErr(err)
	}

	var result map[string]interface{}
	if err := json.Unmarshal(resp, &result); err != nil {
		return diag.FromErr(err)
	}

	if id, ok := result["id"].(string); ok {
		d.SetId(id)
		d.Set("gpo_id", id)
	}
	return nil
}

func resourceGPORead(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	return nil
}

func resourceGPOUpdate(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	return nil
}

func resourceGPODelete(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	client := m.(*apiClient)
	_, err := client.apiRequest(ctx, http.MethodDelete, fmt.Sprintf("/gpos/%s", d.Id()), nil)
	if err != nil {
		return diag.FromErr(err)
	}
	d.SetId("")
	return nil
}

// ---------------------------------------------------------------------------
// Resource: directorynet_dns_record
// ---------------------------------------------------------------------------

func resourceDnsRecord() *schema.Resource {
	return &schema.Resource{
		CreateContext: resourceDnsRecordCreate,
		ReadContext:   resourceDnsRecordRead,
		UpdateContext: resourceDnsRecordUpdate,
		DeleteContext: resourceDnsRecordDelete,
		Schema: map[string]*schema.Schema{
			"zone":    {Type: schema.TypeString, Required: true, Description: "DNS zone name (e.g., example.com)."},
			"name":    {Type: schema.TypeString, Required: true, Description: "Record name relative to zone."},
			"type":    {Type: schema.TypeString, Required: true, Description: "Record type: A, AAAA, CNAME, MX, TXT, SRV, PTR."},
			"value":   {Type: schema.TypeString, Required: true, Description: "Record value (IP address, hostname, etc)."},
			"ttl":     {Type: schema.TypeInt, Optional: true, Default: 3600},
			"priority":{Type: schema.TypeInt, Optional: true, Default: 0, Description: "Priority for MX/SRV records."},
		},
	}
}

func resourceDnsRecordCreate(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	client := m.(*apiClient)
	zone := d.Get("zone").(string)
	body := map[string]interface{}{
		"name":     d.Get("name"),
		"type":     d.Get("type"),
		"value":    d.Get("value"),
		"ttl":      d.Get("ttl"),
		"priority": d.Get("priority"),
	}

	resp, err := client.apiRequest(ctx, http.MethodPost, fmt.Sprintf("/dns/zones/%s/records", zone), body)
	if err != nil {
		return diag.FromErr(err)
	}

	var result map[string]interface{}
	if err := json.Unmarshal(resp, &result); err != nil {
		return diag.FromErr(err)
	}

	id := fmt.Sprintf("%s/%s/%s", zone, d.Get("name"), d.Get("type"))
	d.SetId(id)
	return nil
}

func resourceDnsRecordRead(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	return nil
}

func resourceDnsRecordUpdate(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	return nil
}

func resourceDnsRecordDelete(ctx context.Context, d *schema.ResourceData, m interface{}) diag.Diagnostics {
	client := m.(*apiClient)
	zone := d.Get("zone").(string)
	name := d.Get("name").(string)
	recType := d.Get("type").(string)
	_, err := client.apiRequest(ctx, http.MethodDelete, fmt.Sprintf("/dns/zones/%s/records/%s/%s", zone, name, recType), nil)
	if err != nil {
		return diag.FromErr(err)
	}
	d.SetId("")
	return nil
}
